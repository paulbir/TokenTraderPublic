using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using KucoinConnector.Model;
using Newtonsoft.Json.Linq;
using SharedDataStructures;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SharedTools;
using WebSocket4Net;
using ErrorEventArgs = SuperSocket.ClientEngine.ErrorEventArgs;

namespace KucoinConnector
{
    public class KucoinClient : ITradeConnector
    {
        readonly string restEndpoint = "https://openapi-v2.kucoin.com";
        readonly string serversUrl = "/api/v1/bullet-public";
        readonly string bookSnapshotUrl = "/api/v1/market/orderbook/level2";
        readonly string balancesUrl = "/api/v1/accounts";
        readonly string ordersUrl = "/api/v1/orders";
        readonly string executionsUrl = "/api/v1/fills";
        readonly string symbolsUrl = "/api/v1/symbols";
        readonly char[] topicSeparators = {'/', ':'};
        readonly int executionTimeoutMs = 200;
        readonly decimal adjustFeeCoef = 0.999m;

        readonly Dictionary<string, decimal> minQtysByIsin = new Dictionary<string, decimal>();
        readonly Dictionary<string, BookSnapshotData> snapshotDataByIsin = new Dictionary<string, BookSnapshotData>();
        readonly Dictionary<string, KucoinOrderMessage> ordersByClientId = new Dictionary<string, KucoinOrderMessage>();
        readonly Dictionary<string, KucoinOrderMessage> ordersByExchangeId = new Dictionary<string, KucoinOrderMessage>();
        //readonly ILogger logger;

        System.Timers.Timer timeoutTimer;
        System.Timers.Timer executionTimer;
        System.Timers.Timer pingTimer;
        WebSocket ws;
        List<string> isins;
        string publicKey;
        string secretKey;
        string passphrase;

        bool gotAllSnapshots;
        int requestId;
        long lastExecutionsRequestTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        HttpWebRequest preparedOrder;
        SortedList<string, string> preparedOrderParams;
        string preparedOrderClientId;

        //public KucoinClient(ILogger logger)
        //{
        //    this.logger = logger;
        //}

        public string Name { get; private set; }
        public string ExchangeName => "kucoin";
        public string PublicKey { get; private set; }

        public event EventHandler<OrderMessage> NewOrderAdded;
        public event EventHandler<OrderMessage> OrderCanceled;
        public event EventHandler<OrderMessage> OrderReplaced;
        public event EventHandler<List<OrderMessage>> ActiveOrdersListArrived;
        public event EventHandler<OrderMessage> ExecutionReportArrived;
        public event EventHandler<List<BalanceMessage>> BalanceArrived;
        public event EventHandler<List<PositionMessage>> PositionArrived;
        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler<ErrorMessage> ErrorOccured;
        public event EventHandler<BookMessage> BookUpdateArrived;
        public event EventHandler<BookMessage> BookSnapshotArrived;
        public event EventHandler<TickerMessage> TickerArrived;

        public void Init(List<string> isinsP, int dataTimeoutSeconds, string publicKeyP, string secretKeyP, string connectorName)
        {
            isins = isinsP;
            string[] publicKeyTokens = publicKeyP.Split('_');
            publicKey = publicKeyTokens[0];
            passphrase = publicKeyTokens[1];
            secretKey = secretKeyP;
            PublicKey = publicKey;
            Name = connectorName;

            executionTimer = new System.Timers.Timer(executionTimeoutMs);
            executionTimer.Elapsed += ExecutionTimer_Elapsed;

            if (dataTimeoutSeconds <= 0) return;
            timeoutTimer = new System.Timers.Timer(dataTimeoutSeconds * 1000);
            timeoutTimer.Elapsed += TimeoutTimer_Elapsed;
        }

        public void Start()
        {
            if (!string.IsNullOrEmpty(publicKey) && !string.IsNullOrEmpty(secretKey))
            {
                var symbols = SendRequest<List<Symbol>>("GET", symbolsUrl, false, 0, true);
                foreach (Symbol symbol in symbols) minQtysByIsin.Add(symbol.Isin, symbol.MinOrderQty);
                executionTimer.Start();
            }

            if (isins != null && isins.Count > 0)
            {   
                foreach (string isin in isins) snapshotDataByIsin[isin] = new BookSnapshotData(false, 0);

                var rawServersMessage = SendRequest<RawServers>("POST", serversUrl, false, 0, true);

                string token = rawServersMessage.Token;
                Server server = rawServersMessage.InstanceServers.First(srv => srv.Protocol == "websocket");
                pingTimer = new System.Timers.Timer(server.PingInterval);
                pingTimer.Elapsed += PingTimer_Elapsed;
                //logger.Enqueue($"Going to connect to Kucoin {wsBaseUri} address with bulletToken={bulletToken} for {publicKey} key.");

                ws = new WebSocket($"{server.BaseUri}?token={token}") {EnableAutoSendPing = true, AutoSendPingInterval = 1000};
                ws.Opened += Ws_Opened;
                ws.Closed += Ws_Closed;
                ws.Error += Ws_Error;
                ws.MessageReceived += Ws_MessageReceived;
                ws.Open();
            }
            else Connected?.Invoke(this, null); //данные не получаем, вебсокет не нужен. можно сказать, что законектились
        }

        public void Stop()
        {
            timeoutTimer?.Stop();
            pingTimer?.Stop();
            executionTimer?.Stop();
            ws?.Close();
        }

        public void AddOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            //decimal adjustedQty = qty;
            //if (side == OrderSide.Buy)
            //{
            //    decimal minQty = minQtysByIsin[isin];
            //    adjustedQty = Math.Ceiling(qty / adjustFeeCoef / minQty) * minQty;
            //}
            string addOrderMessageParameters = MessageCreator.CreateAddOrderMessage(clientOrderId, isin, side, price, qty);
           
            var newOrderResponse = SendRequest<NewOrderResponse>("POST", ordersUrl, true, (int)RequestError.AddOrder, false, null, addOrderMessageParameters);

            if (newOrderResponse == null) return;

            //var order = SendRequest<KucoinOrderMessage>("GET", $"/api/v1/orders/{newOrderResponse.ExchangeOrderId}", true, 0, false);
            //Console.ReadLine();
            //var order2 = SendRequest<KucoinOrderMessage>("GET", $"/api/v1/orders/{newOrderResponse.ExchangeOrderId}", true, 0, false);
            //Console.ReadLine();
            //var order3 = SendRequest<KucoinOrderMessage>("GET", $"/api/v1/orders/{newOrderResponse.ExchangeOrderId}", true, 0, false);
            if (!TryPrepareNewOrderAndStore(clientOrderId, isin, side, price, qty, newOrderResponse, out KucoinOrderMessage newOrder)) return;

            NewOrderAdded?.Invoke(this, newOrder);
        }

        public void CancelOrder(string clientOrderId, int requestId)
        {
            if (!TryRemoveFromOrdersDic(ordersByClientId, clientOrderId, out KucoinOrderMessage orderToCancel, "clientId")) return;
            if (!TryRemoveFromOrdersDic(ordersByExchangeId, orderToCancel.ExchangeOrderId, out _, "exchangeId")) return;

            SendRequest<NewOrderResponse>("DELETE", $"{ordersUrl}/{orderToCancel.ExchangeOrderId}", true, (int)RequestError.CancelOrder, false);

            orderToCancel.SetStatus("canceled");
            OrderCanceled?.Invoke(this, orderToCancel);
        }

        public void GetActiveOrders(int requestId)
        {
            var activeOrders = SendRequest<RawActiveOrders>("GET",
                                                            ordersUrl,
                                                            true,
                                                            (int)RequestError.ActiveOrders,
                                                            false,
                                                            new SortedList<string, string> {{"status", "active"}});
            if (activeOrders == null) return;

            foreach (KucoinOrderMessage order in activeOrders.Orders)
            {
                ordersByExchangeId.TryAdd(order.ExchangeOrderId, order);
                ordersByClientId.TryAdd(order.OrderId, order);
            }

            ActiveOrdersListArrived?.Invoke(this, activeOrders.Orders.Select(order => (OrderMessage)order).ToList());            
        }

        public void GetPosAndMoney(int requestId)
        {
            var balances = SendRequest<List<KucoinBalanceMessage>>("GET", balancesUrl, true, (int)RequestError.TradingBalance, false);

            BalanceArrived?.Invoke(this, balances.Where(balance => balance.Type == "trade").Select(balance => (BalanceMessage)balance).ToList());
        }

        public void PrepareOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            //decimal adjustedQty = qty;
            //if (side == OrderSide.Buy)
            //{
            //    decimal minQty = minQtysByIsin[isin];
            //    adjustedQty = Math.Ceiling(qty / adjustFeeCoef / minQty) * minQty;
            //}
            string addOrderMessageParameters = MessageCreator.CreateAddOrderMessage(clientOrderId, isin, side, price, qty);
            preparedOrderParams = new SortedList<string, string>
                                  {
                                      {"isin", isin},
                                      {"side", side == OrderSide.Buy ? "BUY" : "SELL"},
                                      {"price", price.ToString(CultureInfo.InvariantCulture)},
                                      {"qty", qty.ToString(CultureInfo.InvariantCulture)}
                                  };
            preparedOrder = CreateHttpWebRequest("POST", ordersUrl, true, null, addOrderMessageParameters);
            preparedOrderClientId = clientOrderId;
        }

        public void ReplaceOrder(string oldClientOrderId, string newClientOrderId, decimal price, decimal qty, int requestId)
        {
            throw new NotImplementedException();
        }

        public Task SendPreparedOrder()
        {
            return Task.Run(() =>
                            {
                                if (preparedOrder == null || preparedOrderParams == null || string.IsNullOrEmpty(preparedOrderClientId)) return;

                                string newOrderResponseString = GetResponse(preparedOrder, ordersUrl, (int)RequestError.AddOrder);
                                JToken token = JToken.Parse(newOrderResponseString);
                                if (!CheckSuccess(token, (int)RequestError.AddOrder, false)) return;
                                NewOrderResponse newOrderResponse;

                                try
                                {
                                    newOrderResponse = token.SelectToken("data").ToObject<NewOrderResponse>();
                                }
                                catch (Exception e)
                                {
                                    e.Data["response"] = newOrderResponseString;
                                    throw;
                                }

                                OrderSide side = preparedOrderParams["side"] == "BUY" ? OrderSide.Buy : OrderSide.Sell;
                                if (!TryPrepareNewOrderAndStore(preparedOrderClientId,
                                                                preparedOrderParams["isin"],
                                                                side,
                                                                preparedOrderParams["price"].ToDecimal(),
                                                                preparedOrderParams["qty"].ToDecimal(),
                                                                newOrderResponse,
                                                                out KucoinOrderMessage newOrder)) return;

                                NewOrderAdded?.Invoke(this, newOrder);

                                preparedOrder = null;
                                preparedOrderParams = null;
                                preparedOrderClientId = "";
                            });
        }

        void TimeoutTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            timeoutTimer?.Stop();
            Disconnected?.Invoke(this, null);
        }

        void ExecutionTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            executionTimer.Stop();

            try
            {
                var rawExecutions = SendRequest<RawExecutedOrders>("GET",
                                                                   executionsUrl,
                                                                   true,
                                                                   (int)RequestError.Executions,
                                                                   false,
                                                                   new SortedList<string, string> {{"startAt", lastExecutionsRequestTimestamp.ToString()}});
                if (rawExecutions == null) return;

                lastExecutionsRequestTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                foreach (KucoinExecutedOrderMessage executedOrder in rawExecutions.ExecutedOrders)
                {
                    if (executedOrder.ExchangeOrderId == null) ErrorOccured?.Invoke(this, new KucoinErrorMessage((int)RequestError.Executions, $"{executedOrder} has null exchangeId", ""));

                    if (!ordersByExchangeId.TryGetValue(executedOrder.ExchangeOrderId, out KucoinOrderMessage order)) continue;

                    if (order.OrderId == null) ErrorOccured?.Invoke(this, new KucoinErrorMessage((int)RequestError.Executions, $"{order} has null orderId", ""));

                    if (executedOrder.TradeQty == order.Qty) //полностью забрали
                    {
                        order.SetStatus("filled");
                        TryRemoveFromOrdersDic(ordersByExchangeId, executedOrder.ExchangeOrderId, out _, "exchangeId");
                        TryRemoveFromOrdersDic(ordersByClientId, order.OrderId, out _, "clientId");
                    }
                    else //частично забрали
                    {
                        Console.WriteLine($"order: {order} | executedOrder: {executedOrder}");
                        order.SetStatus("partially_filled");
                        order.DesreaseQty(executedOrder.TradeQty);
                    }

                    executedOrder.SetOrderIdAndQty(order.OrderId, order.Qty);

                    ExecutionReportArrived?.Invoke(this, executedOrder);
                }
            }
            catch (Exception ex)
            {
                ErrorOccured?.Invoke(this, new KucoinErrorMessage((int)RequestError.Executions, ex.Message, ""));
            }
            finally
            {
                executionTimer.Start();
            }
        }

        void PingTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            ws.Send(MessageCreator.CreatePingMessage(requestId++));
        }

        T SendRequest<T>(string method, string command, bool needSign, int possibleErrorCode, bool throwOnError, SortedList<string, string> parameters = null, string jsonParameters = null)
        {
            if (parameters == null) parameters = new SortedList<string, string>();

            string jsonString = QueryString(method, command, needSign, possibleErrorCode, parameters, jsonParameters);
            
            if (string.IsNullOrEmpty(jsonString)) return default(T);
            JToken token = JToken.Parse(jsonString);
            if (!CheckSuccess(token, possibleErrorCode, throwOnError)) return default(T);
            T output;

            try
            {
                output = token.SelectToken("data").ToObject<T>();
            }
            catch (Exception e)
            {
                e.Data["response"] = jsonString;
                throw;
            }

            if (output is KucoinOrderMessage) Console.WriteLine(jsonString);
            return output;
        }

        string QueryString(string method, string relativeUrl, bool needSign, int possibleErrorCode, SortedList<string, string> parameters, string jsonParameters)
        {
            HttpWebRequest request = CreateHttpWebRequest(method, relativeUrl, needSign, parameters, jsonParameters);
            return GetResponse(request, relativeUrl, possibleErrorCode);
        }

        string GetResponse(HttpWebRequest request, string relativeUrl, int possibleErrorCode)
        {
            WebResponse response;
            try
            {
                response = request.GetResponse();
            }
            catch (WebException ex)
            {
                using (var exResponse = (HttpWebResponse)ex.Response)
                {
                    ex.Data["Request"] = relativeUrl;
                    Stream stream = exResponse?.GetResponseStream();
                    if (stream == null) throw;

                    using (var sr = new StreamReader(stream))
                    {
                        string responseString = sr.ReadToEnd();
                        ex.Data["Response"] = responseString;


                        ErrorOccured?.Invoke(this, new KucoinErrorMessage(possibleErrorCode, responseString, ex.Message));
                        return null;
                    }
                }
            }

            using (var sr = new StreamReader(response.GetResponseStream(), Encoding.ASCII))
            {
                string responseString = sr.ReadToEnd();
                return responseString;
            }
        }

        HttpWebRequest CreateHttpWebRequest(string method, string relativeUrl, bool needSign, SortedList<string, string> parameters, string jsonParameters)
        {
            string paramsString = parameters == null ? null : string.Join('&', parameters.Select(pair => $"{pair.Key}={pair.Value}"));
            string urlWithParams = relativeUrl + (string.IsNullOrEmpty(paramsString) ? "" : "?") + paramsString;

            HttpWebRequest request = WebRequest.CreateHttp($"{restEndpoint}{urlWithParams}");

            request.Method = method;
            request.Timeout = Timeout.Infinite;

            if (needSign)
            {
                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                string signature = MakeSignature(timestamp, method, urlWithParams, jsonParameters);
                request.Headers["KC-API-KEY"] = publicKey;
                request.Headers["KC-API-PASSPHRASE"] = passphrase;
                request.Headers["KC-API-TIMESTAMP"] = timestamp.ToString();
                request.Headers["KC-API-SIGN"] = signature;
            }

            if (method == "POST")
            {
                request.ContentType = "application/json";
                if (string.IsNullOrEmpty(jsonParameters)) return request;

                using (Stream stream = request.GetRequestStream())
                {
                    byte[] bodyBytes = Encoding.ASCII.GetBytes(jsonParameters);
                    stream.Write(bodyBytes, 0, bodyBytes.Length);
                }
            }

            return request;
        }

        string MakeSignature(long timestamp, string method, string relativeUrl, string paramsString)
        {
            string toSign = $"{timestamp}{method}{relativeUrl}{paramsString}";

            byte[] messageBytes = Encoding.UTF8.GetBytes(toSign);
            byte[] secretKeyBytes = Encoding.UTF8.GetBytes(secretKey);

            byte[] hashBytes;
            using (var encoder = new HMACSHA256(secretKeyBytes))
            {
                hashBytes = encoder.ComputeHash(messageBytes);
            }

            return Convert.ToBase64String(hashBytes);
        }

        void Ws_Opened(object sender, EventArgs e)
        {
            ws.Send(MessageCreator.CreateSubscribeToBookMessage(isins, requestId++));
            ws.Send(MessageCreator.CreateSubscribeToSymbolSnapshotMessage(isins, requestId++));
            timeoutTimer?.Start();
            pingTimer?.Start();

            Connected?.Invoke(this, null);
        }

        void Ws_Closed(object sender, EventArgs e)
        {
            timeoutTimer?.Stop();
            pingTimer?.Stop();
            executionTimer?.Stop();
            Disconnected?.Invoke(this, null);
        }

        void Ws_Error(object sender, ErrorEventArgs e)
        {
            throw e.Exception;
        }

        void Ws_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            JToken token = JToken.Parse(e.Message);
            string type = (string)token.SelectToken("type");

            if (type != "message") return;


            string topic = (string)token.SelectToken("topic");
            JToken dataToken = token.SelectToken("data");
            ProcessMessage(topic, dataToken);
        }

        void ProcessMessage(string topic, JToken dataToken)
        {
            timeoutTimer?.Stop();

            string[] topicTokens = topic.Split(topicSeparators);
            string messageType = topicTokens[2];
            string isin = topicTokens[3];

            switch (messageType)
            {
                case "level2":
                    ProcessBook(isin, dataToken);
                    break;
                case "snapshot":
                    ProcessTick(dataToken);
                    break;
            }

            timeoutTimer?.Start();
        }

        void ProcessBook(string isin, JToken dataToken)
        {
            if (!snapshotDataByIsin.TryGetValue(isin, out BookSnapshotData snapshotData)) return;

            if (!gotAllSnapshots && !snapshotData.GotSnapshot)
            {
                var snapshot = SendRequest<KucoinBookSnapshotMessage>("GET",
                                                                  bookSnapshotUrl,
                                                                  false,
                                                                  0,
                                                                  true,
                                                                  new SortedList<string, string> {{"symbol", isin}});
                snapshot.SetIsin(isin);
                snapshotDataByIsin[isin].SetSnapshot(snapshot.Sequence);
                if (snapshotDataByIsin.Values.All(sntData => sntData.GotSnapshot)) gotAllSnapshots = true;
                BookSnapshotArrived?.Invoke(this, snapshot);
            }

            var update = dataToken.ToObject<KucoinBookUpdateMessage>();

            if (update.SequenceStart <= snapshotData.Sequence) return;

            update.SetIsin(isin);
            BookUpdateArrived?.Invoke(this, update);
        }

        void ProcessTick(JToken dataToken)
        {
            var tick = dataToken.SelectToken("data").ToObject<KucoinTickMessage>();
            TickerArrived?.Invoke(this, tick);
        }

        bool CheckSuccess(JToken token, int errorCode, bool throwOnError)
        {
            string code = (string)token.SelectToken("code");

            if (!string.IsNullOrEmpty(code) && code != "200000")
            {
                string message = (string)token.SelectToken("msg");

                if (throwOnError)
                {
                    var ex = new RequestFailedException("Requesting servers failed.");
                    ex.Data["Code"] = code;
                    ex.Data["Message"] = message;
                    throw ex;
                }

                var error = new KucoinErrorMessage(errorCode, code, message);
                ErrorOccured?.Invoke(this, error);
                return false;
            }

            return true;
        }

        bool TryPrepareNewOrderAndStore(string clientOrderId,
                                        string isin,
                                        OrderSide side,
                                        decimal price,
                                        decimal qty,
                                        NewOrderResponse newOrderResponse,
                                        out KucoinOrderMessage newOrder)
        {
            string sideStr = side == OrderSide.Buy ? "buy" : "sell";
            newOrder = new KucoinOrderMessage(newOrderResponse.ExchangeOrderId,
                                              clientOrderId,
                                              isin,
                                              sideStr,
                                              price,
                                              qty,
                                              0,
                                              0,
                                              DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                                              true);

            if (!TryAddToOrdersDic(ordersByClientId, clientOrderId, newOrder, "clientId")) return false;
            if (!TryAddToOrdersDic(ordersByExchangeId, newOrder.ExchangeOrderId, newOrder, "exchangeId")) return false;
            return true;
        }

        bool TryAddToOrdersDic(Dictionary<string, KucoinOrderMessage> ordersDic, string orderId, KucoinOrderMessage newOrder, string dictionarуName)
        {
            if (!ordersDic.TryAdd(orderId, newOrder))
            {
                ErrorOccured?.Invoke(this,
                                     new KucoinErrorMessage((int)RequestError.AddOrder,
                                                            $"Orders by {dictionarуName} dictionary already contains order with id={orderId}",
                                                            newOrder.ToString()));
                return false;
            }

            return true;
        }

        bool TryRemoveFromOrdersDic(Dictionary<string, KucoinOrderMessage> ordersDic,
                                    string orderId,
                                    out KucoinOrderMessage orderToCancel,
                                    string dictionarуName)
        {
            if (!ordersDic.Remove(orderId, out orderToCancel))
            {
                ErrorOccured?.Invoke(this,
                                     new KucoinErrorMessage((int)RequestError.CancelOrder,
                                                            $"OrderId={orderId} was not found in {dictionarуName} orders dictionary.",
                                                            ""));
                return false;
            }

            return true;
        }
    }
}
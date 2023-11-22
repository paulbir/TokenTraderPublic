using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using DutyFlyConnector.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharedDataStructures;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using WebSocket4Net;
using ErrorEventArgs = SuperSocket.ClientEngine.ErrorEventArgs;
using Timer = System.Timers.Timer;
namespace DutyFlyConnector
{
    public class DutyFlyClient : ITradeConnector
    {
        static readonly string wsUrl = "wss://engine.dftrades.com/ws";
        static readonly string restBaseUrl = "https://engine.dftrades.com/api";
        static readonly string consumerId = "---";

        readonly string loginUrl = "/auth/login";
        //readonly string activeOrdersUrl = "/orders/open-orders";
        readonly string activeOrdersUrl = "/orders";
        readonly string addOrderUrl = "/orders";
        readonly string cancelOrderUrl = "/orders";

        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler<ErrorMessage> ErrorOccured;
        public event EventHandler<BookMessage> BookUpdateArrived;
        public event EventHandler<BookMessage> BookSnapshotArrived;
        public event EventHandler<TickerMessage> TickerArrived;
        public event EventHandler<OrderMessage> NewOrderAdded;
        public event EventHandler<OrderMessage> OrderCanceled;
        public event EventHandler<OrderMessage> OrderReplaced;
        public event EventHandler<List<OrderMessage>> ActiveOrdersListArrived;
        public event EventHandler<OrderMessage> ExecutionReportArrived;
        public event EventHandler<List<BalanceMessage>> BalanceArrived;
        public event EventHandler<List<PositionMessage>> PositionArrived;

        public string Name { get; private set; }
        public string ExchangeName => "dutyfly";
        public string PublicKey { get; private set; }

        List<string> isins;
        string publicKey;
        string secretKey;

        Timer timeoutTimer;
        WebSocket ws;

        readonly Dictionary<string, IsinData> isinDatas = new Dictionary<string, IsinData>();
        readonly Dictionary<string, DutyFlyOrderMessage> ordersByExchangeId = new Dictionary<string, DutyFlyOrderMessage>();
        readonly Dictionary<string, DutyFlyOrderMessage> ordersByClientId = new Dictionary<string, DutyFlyOrderMessage>();

        string authToken;
        string userId;
        HttpWebRequest preparedOrder;
        string preparedOrderClientId;
        string preparedBody;

        public void Init(List<string> isinsP, int dataTimeoutSeconds, string publicKeyP, string secretKeyP, string connectorName)
        {
            isins = isinsP;
            publicKey = publicKeyP;
            secretKey = secretKeyP;
            PublicKey = publicKeyP;

            Name = connectorName;

            if (dataTimeoutSeconds <= 0) return;
            timeoutTimer = new Timer(dataTimeoutSeconds * 1000);
            timeoutTimer.Elapsed += TimeoutTimer_Elapsed;
        }

        public void Start()
        {
            var logonResponse = SendRequest<LogonResponse>("POST", loginUrl, $"{publicKey}:{secretKey}", 0);
            if (logonResponse == null) throw new RequestFailedException($"REST logon failed for publicKey={publicKey} for connector {Name}.");

            authToken = logonResponse.AuthToken;
            userId = logonResponse.UserId;

            ws = new WebSocket(wsUrl) { EnableAutoSendPing = true, AutoSendPingInterval = 1000 };

            ws.Opened += Ws_Opened;
            ws.Closed += Ws_Closed;
            ws.Error += Ws_Error;
            ws.MessageReceived += Ws_MessageReceived;
            ws.Open();
        }

        public void Stop()
        {
            ClearOnStop();
            ws.Close();
        }

        public void AddOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            string sideStr = side == OrderSide.Buy ? "buy" : "sell";
            string priceStr = price.ToString(CultureInfo.InvariantCulture);
            string qtyStr = qty.ToString(CultureInfo.InvariantCulture);
            var newOrder = SendRequest<DutyFlyOrderMessage>("POST",
                                        addOrderUrl,
                                        authToken,
                                        (int)RequestError.AddOrder,
                                        $"symbol={isin}",
                                        $"side={sideStr}",
                                        "type=limit",
                                        $"price={priceStr}",
                                        $"quantity={qtyStr}");

            if (newOrder == null) return;

            ordersByClientId.Add(clientOrderId, newOrder);
            ordersByExchangeId.Add(newOrder.ExchangeOrderId, newOrder);

            newOrder.SetOrderId(clientOrderId);
            NewOrderAdded?.Invoke(this, newOrder);
            if (newOrder.Status == "filled")
            {
                newOrder.SetTrade(0);
                ExecutionReportArrived?.Invoke(this, newOrder);
                return;
            }

            Task.Run(() =>
                     {
                         Thread.Sleep(50);
                         CheckOrderUpdates(newOrder.ExchangeOrderId, clientOrderId);
                     });
        }

        public void PrepareOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            string sideStr = side == OrderSide.Buy ? "buy" : "sell";
            string priceStr = price.ToString(CultureInfo.InvariantCulture);
            string qtyStr = qty.ToString(CultureInfo.InvariantCulture);
            string body = CreateBody(new object[]
                                     {
                                         $"symbol={isin}",
                                         $"side={sideStr}",
                                         "type=limit",
                                         $"price={priceStr}",
                                         $"quantity={qtyStr}"
                                     });
            preparedOrder = CreateHttpWebRequest("POST", addOrderUrl, body, authToken);
            preparedOrderClientId = clientOrderId;
            preparedBody = body;
        }

        public Task SendPreparedOrder()
        {
            return Task.Run(() =>
                     {
                         if (preparedOrder == null || preparedOrderClientId == null || preparedBody == null) return;
                         string jsonResponse = GetResponse(preparedOrder, $"{addOrderUrl}?{preparedOrder}");
                         var newOrder = ParseJson<DutyFlyOrderMessage>(addOrderUrl, (int)RequestError.AddOrder, jsonResponse, preparedBody);
                         if (newOrder == null) return;

                         ordersByClientId.Add(preparedOrderClientId, newOrder);
                         ordersByExchangeId.Add(newOrder.ExchangeOrderId, newOrder);

                         newOrder.SetOrderId(preparedOrderClientId);
                         NewOrderAdded?.Invoke(this, newOrder);

                         if (newOrder.Status == "filled")
                         {
                             newOrder.SetTrade(0);
                             ExecutionReportArrived?.Invoke(this, newOrder);
                             preparedOrder = null;
                             preparedOrderClientId = null;
                             preparedBody = null;
                             return;
                         }

                         Thread.Sleep(50);
                         CheckOrderUpdates(newOrder.ExchangeOrderId, preparedOrderClientId);
                         preparedOrder = null;
                         preparedOrderClientId = null;
                         preparedBody = null;
                     });
        }

        public void CancelOrder(string clientOrderId, int requestId)
        {
            if (!ordersByClientId.TryGetValue(clientOrderId, out DutyFlyOrderMessage storedOrder))
            {
                ErrorOccured?.Invoke(this, new DutyFlyErrorMessage((int)RequestError.CancelOrder, $"Couldn't find orderId={clientOrderId} in dictionary.", clientOrderId));
                return;
            }

            var canceledOrder =
                SendRequest<DutyFlyOrderMessage>("DELETE", $"{cancelOrderUrl}/{storedOrder.ExchangeOrderId}", authToken, (int)RequestError.CancelOrder);

            if (canceledOrder == null)
            {
                ErrorOccured?.Invoke(this, new DutyFlyErrorMessage((int)RequestError.CancelOrder, $"Couldn't cancel order with orderId={clientOrderId}.", clientOrderId));
                return;
            }

            OrderCanceled?.Invoke(this, canceledOrder);
        }

        public void ReplaceOrder(string oldClientOrderId, string newClientOrderId, decimal price, decimal qty, int requestId)
        {
            throw new NotImplementedException();
        }

        public void GetActiveOrders(int requestId)
        {
            var activeOrders = SendRequest<List<DutyFlyOrderMessage>>("GET", activeOrdersUrl, authToken, (int)RequestError.ActiveOrders);
            foreach (DutyFlyOrderMessage order in activeOrders)
            {
                //если эту заявку уже добавляли, то она есть в ordersByExchangeId и соответственно должна быть в ordersByClientId.
                // но clietnOrderId я сейчас не знаю, поэтому проверяем только ordersByExchangeId.
                if (ordersByExchangeId.TryAdd(order.ExchangeOrderId, order)) ordersByClientId.TryAdd(order.ExchangeOrderId, order);
            }

            ActiveOrdersListArrived?.Invoke(this, activeOrders.Select(order => (OrderMessage)order).ToList());
        }

        public void GetPosAndMoney(int requestId)
        {
            //string getBalancesMessage = MessageCreator.CreateGetBalancesMessage(requestId);
            //ws.Send(getBalancesMessage);

            var rawBalances = SendRequest<BalancesRaw>("GET", $"/user/{userId}/balances", authToken, (int)RequestError.TradingBalance);
            IEnumerable<string> positiveAvailableCurrencies = rawBalances.Available.Where(pair => pair.Value > 0).Select(pair => pair.Key);
            IEnumerable<string> positiveTotalCurrencies = rawBalances.Total.Where(pair => pair.Value > 0).Select(pair => pair.Key);
            IEnumerable<string> positiveCurrenciesIntersection = positiveAvailableCurrencies.Intersect(positiveTotalCurrencies);

            var balances = new List<BalanceMessage>();
            foreach (string currency in positiveCurrenciesIntersection)
                balances.Add(new DutyFlyBalanceMessage(currency, rawBalances.Available[currency], rawBalances.Total[currency]));

            BalanceArrived?.Invoke(this, balances);
        }

        void TimeoutTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            timeoutTimer?.Stop();
            Disconnected?.Invoke(this, null);
        }

        T SendRequest<T>(string method, string path, string authString, int possibleErrorCode, params object[] parameters)
        {
            string body = CreateBody(parameters);
            string jsonString = QueryString(method, path, body, authString);

            return ParseJson<T>(path, possibleErrorCode, jsonString, body);
        }

        static string CreateBody(object[] parameters)
        {
            return parameters?.Length != 0 ? string.Join("&", parameters) : null;
        }

        string QueryString(string method, string path, string body, string authString)
        {
            HttpWebRequest request = CreateHttpWebRequest(method, path, body, authString);
            return GetResponse(request, $"{path}?{body}");
        }

        HttpWebRequest CreateHttpWebRequest(string method, string path, string body, string authString)
        {
            string requestUrl;
            if (method == "GET" && !string.IsNullOrEmpty(body)) requestUrl = $"{restBaseUrl}{path}?{Uri.EscapeUriString(body)}";
            else requestUrl = $"{restBaseUrl}{path}";

            HttpWebRequest request = WebRequest.CreateHttp(requestUrl);
            request.Method = method;
            request.Timeout = Timeout.Infinite;
            request.Headers["Consumer"] = $"sx <{consumerId}>";
            request.Headers["Authorization"] = $"sx <{authString}>";

            if (body != null && method != "GET")
            {
                request.ContentType = "application/x-www-form-urlencoded";
                using (Stream stream = request.GetRequestStream())
                {
                    byte[] bodyBytes = Encoding.ASCII.GetBytes(body);
                    stream.Write(bodyBytes, 0, bodyBytes.Length);
                }
            }

            return request;
        }

        string GetResponse(HttpWebRequest request, string relativeUrl)
        {
            WebResponse response = null;
            try
            {
                response = request.GetResponse();
            }
            catch (WebException ex)
            {
                using (var exResponse = (HttpWebResponse)ex.Response)
                {
                    using (var sr = new StreamReader(exResponse.GetResponseStream() ?? throw ex))
                    {
                        string responseString = sr.ReadToEnd();
                        throw new RequestFailedException($"{relativeUrl} request failed: {responseString}.");
                    }
                }
            }

            using (var sr = new StreamReader(response?.GetResponseStream() ?? throw new InvalidOperationException($"{relativeUrl} response is null."), Encoding.ASCII))
            {
                string responseString = sr.ReadToEnd();
                return responseString;
            }
        }

        T ParseJson<T>(string path, int possibleErrorCode, string jsonString, string body)
        {
            JToken token = JToken.Parse(jsonString);
            bool isSuccess = (bool)token.SelectToken("status");
            if (!isSuccess)
            {
                string errorMessage = (string)token.SelectToken("message");
                ErrorOccured?.Invoke(this, new DutyFlyErrorMessage(possibleErrorCode, errorMessage, path + body));
                return default;
            }

            T output;

            try
            {
                output = token.SelectToken("result").ToObject<T>();
            }
            catch (Exception e)
            {
                e.Data["Response"] = jsonString;
                throw;
            }

            return output;
        }

        void Ws_Opened(object sender, EventArgs e)
        {
            string authorizeMessage = MessageCreator.CreateAuthorizeMessage(authToken, consumerId, 0);
            ws.Send(authorizeMessage);
        }

        void Ws_Closed(object sender, EventArgs e)
        {
            ClearOnStop();
            Disconnected?.Invoke(this, null);
        }

        void Ws_Error(object sender, ErrorEventArgs e)
        {
            throw e.Exception;
        }

        void Ws_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            JObject messageObject = JObject.Parse(e.Message);
            string type = (string)messageObject.SelectToken("method");
            switch (type)
            {
                case "authorize":
                    ProcessAuthorize(messageObject);
                    break;

                case "snapshotBook":
                    ProcessSnapshot(messageObject);
                    break;

                case "bookUpdate":
                    timeoutTimer?.Stop();
                    ProcessUpdate(messageObject);
                    timeoutTimer?.Start();
                    break;

                case "newTrade":
                    timeoutTimer?.Stop();
                    ProcessTrade(messageObject);
                    timeoutTimer?.Start();
                    break;

                //case "balances":
                //    ProcessBalances(messageObject);
                //    break;
            }
        }

        void ProcessAuthorize(JObject messageObject)
        {
            bool isSuccess = (bool)messageObject.SelectToken("result").SelectToken("success");
            if (!isSuccess) throw new RequestFailedException($"Websocket authorization failed for publicKey={publicKey} for connector {Name}.");

            if (isins != null && isins.Count > 0)
            {
                foreach (string isin in isins)
                {
                    isinDatas[isin] = new IsinData();
                    string isinReplaced = isin.Replace("-", "/");

                    string subscribeBookMessage = MessageCreator.CreateSubscribeBookMessage(isinReplaced, 0);
                    ws.Send(subscribeBookMessage);

                    string subscribeTradesMessage = MessageCreator.CreateSubscribeTradesMessage(isinReplaced, 0);
                    ws.Send(subscribeTradesMessage);
                }

                timeoutTimer?.Start();
            }

            Connected?.Invoke(this, null);
        }

        void ProcessSnapshot(JObject messageObject)
        {
            DutyFlyBookMessage book = ParseBook(messageObject);
            BookSnapshotArrived?.Invoke(this, book);

            if (!isinDatas.TryGetValue(book.Isin, out IsinData isinData)) return;
            isinData.Bid = book.BestBid;
            isinData.Ask = book.BestAsk;
        }

        void ProcessUpdate(JObject messageObject)
        {
            DutyFlyBookMessage book = ParseBook(messageObject);
            BookUpdateArrived?.Invoke(this, book);

            if (!isinDatas.TryGetValue(book.Isin, out IsinData isinData)) return;
            if (book.BestBid > 0) isinData.Bid = book.BestBid;
            if (book.BestAsk > 0) isinData.Ask = book.BestAsk;
            TickerArrived?.Invoke(this, new DutyFlyTickerMessage(book.Isin, isinData.Bid, isinData.Ask, isinData.Last));
        }

        void ProcessTrade(JObject messageObject)
        {
            JToken resultToken = messageObject.SelectToken("result");
            string isin = (string)resultToken.SelectToken("pair");
            decimal price = (decimal)resultToken.SelectToken("price");

            if (!isinDatas.TryGetValue(isin, out IsinData isinData)) return;
            isinData.Last = price;
            TickerArrived?.Invoke(this, new DutyFlyTickerMessage(isin, isinData.Bid, isinData.Ask, isinData.Last));
        }

        DutyFlyBookMessage ParseBook(JObject messageObject) => messageObject.SelectToken("result").ToObject<DutyFlyBookMessage>();

        void ClearOnStop()
        {
            timeoutTimer?.Stop();
            isinDatas.Clear();
        }

        void CheckOrderUpdates(string orderId, string clientOrderId)
        {
            var order = SendRequest<DutyFlyOrderMessage>("GET", $"{activeOrdersUrl}/{orderId}", authToken, (int)RequestError.Executions);
            if (order == null) return;

            if (order.Status == "cancelled")
            {
                OrderCanceled?.Invoke(this, order);
                RemoveOrder(orderId, clientOrderId);
            }
            else
            {
                if (!ordersByExchangeId.TryGetValue(orderId, out DutyFlyOrderMessage storedOrder)) return;
                if (order.CumulativeTradedQty > storedOrder.CumulativeTradedQty)
                {
                    order.SetOrderId(clientOrderId);
                    order.SetTrade(storedOrder.CumulativeTradedQty);
                    ExecutionReportArrived?.Invoke(this, order);
                }

                if (order.Status == "filled" || order.CumulativeTradedQty == order.Qty) RemoveOrder(orderId, clientOrderId);
            }
        }

        void RemoveOrder(string orderId, string clientOrderId)
        {
            ordersByExchangeId.Remove(orderId);
            ordersByClientId.Remove(clientOrderId);
        }
    }
}

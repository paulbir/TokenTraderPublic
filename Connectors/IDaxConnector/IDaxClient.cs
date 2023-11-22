using System;
using System.Collections.Concurrent;
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
using IDaxConnector.Model;
using Newtonsoft.Json;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SharedDataStructures;
using SharedTools;
using static System.Net.SecurityProtocolType;
using Timer = System.Timers.Timer;

namespace IDaxConnector
{
    // ReSharper disable once InconsistentNaming
    public class IDaxClient : ITradeConnector
    {
        readonly int pricesTimerIntervalMs = 1;
        readonly string baseUrl = "https://openapi.idax.mn/api/v1/";
        //readonly string baseUrl = "https://testopenapi.idax.mn/api/v1/";
        readonly Encoding encoding = Encoding.ASCII;

        readonly ConcurrentDictionary<string, IDaxOrderMessage> ordersByHash = new ConcurrentDictionary<string, IDaxOrderMessage>();
        readonly ConcurrentDictionary<string, string> orderIdsByClientIds = new ConcurrentDictionary<string, string>();
        readonly HashSet<IDaxOrderMessage> executedOrders = new HashSet<IDaxOrderMessage>();
        HttpWebRequest preparedOrder;
        SortedList<string, string> preparedOrderParams;
        string preparedOrderClientId;

        List<string> isins;
        string publicKey;
        string secretKey;
        Timer pricesTimer;

        public string Name { get; private set; }
        public string ExchangeName => "idax";
        public string PublicKey { get; private set; }

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

        public void Init(List<string> isinsP, int dataTimeoutSeconds, string publicKeyP, string secretKeyP, string connectorName)
        {
            isins = isinsP;
            Name = connectorName;
            publicKey = publicKeyP;
            secretKey = secretKeyP;
            PublicKey = publicKeyP;

            if (isinsP != null)
            {
                pricesTimer = new Timer(pricesTimerIntervalMs);
                pricesTimer.Elapsed += BookTimer_Elapsed;
            }
        }

        public void Start()
        {
            if (isins != null) pricesTimer.Start();
            Connected?.Invoke(this, null);
        }

        public void Stop()
        {
            pricesTimer.Stop();
            Disconnected?.Invoke(this, null);
        }

        public void AddOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            var orderParams = new SortedList<string, string>
                              {
                                  {"orderSide", side == OrderSide.Buy ? "1" : "2"},
                                  {"orderType", "1"},
                                  {"pair", isin},
                                  {"price", price.ToString(CultureInfo.InvariantCulture)},
                                  {"amount", qty.ToString(CultureInfo.InvariantCulture)}
                              };
            var newOrderIdMessage = PostData<RawNewOrderIdMessage>("createorder", true, orderParams);
            if (newOrderIdMessage.Success)
            {
                var newOrder = new IDaxOrderMessage(clientOrderId,
                                                    side == OrderSide.Buy ? 1 : 2,
                                                    isin,
                                                    price,
                                                    qty,
                                                    0,
                                                    DateTime.UtcNow.ToString(CultureInfo.InvariantCulture));

                ordersByHash.TryAdd(newOrder.StringHash, newOrder);
                orderIdsByClientIds.TryAdd(clientOrderId, newOrderIdMessage.OrderId);
                NewOrderAdded?.Invoke(this, newOrder);

                //эмулируем ExecutionReport. ждём 100мс, чтобы сделки прошли. а потом получаем их список и пытаемся для 
                // добавленных в dictionary заявок выставить проторгованное количество по сделкам.
                Task.Run(() =>
                         {
                             Thread.Sleep(300);
                             SendExecutionReportsFromMyLastTrades();
                         });
            }
            else ErrorOccured?.Invoke(this, new IDaxErrorMessage(0, newOrderIdMessage.Message, ""));
        }

        public void PrepareOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            var orderParams = new SortedList<string, string>
                              {
                                  {"orderSide", side == OrderSide.Buy ? "1" : "2"},
                                  {"orderType", "1"},
                                  {"pair", isin},
                                  {"price", price.ToString(CultureInfo.InvariantCulture)},
                                  {"amount", qty.ToString(CultureInfo.InvariantCulture)}
                              };

            preparedOrder = CreateHttpWebRequest("POST", "createorder", true, orderParams);
            preparedOrderParams = orderParams;
            preparedOrderClientId = clientOrderId;
        }

        public Task SendPreparedOrder()
        {
            return Task.Run(() =>
                            {
                                if (preparedOrder == null || preparedOrderParams == null || string.IsNullOrEmpty(preparedOrderClientId)) return;

                                string newOrderMessageString = GetResponse(preparedOrder, "createorder", preparedOrderParams);
                                var newOrderIdMessage = JsonConvert.DeserializeObject<RawNewOrderIdMessage>(newOrderMessageString);

                                if (newOrderIdMessage.Success)
                                {
                                    var newOrder = new IDaxOrderMessage(preparedOrderClientId,
                                                                        Convert.ToInt32(preparedOrderParams["orderSide"]),
                                                                        preparedOrderParams["pair"],
                                                                        preparedOrderParams["price"].ToDecimal(),
                                                                        preparedOrderParams["amount"].ToDecimal(),
                                                                        0,
                                                                        DateTime.UtcNow.ToString(CultureInfo.InvariantCulture));

                                    ordersByHash.TryAdd(newOrder.StringHash, newOrder);
                                    orderIdsByClientIds.TryAdd(preparedOrderClientId, newOrder.OrderId);
                                    NewOrderAdded?.Invoke(this, newOrder);

                                    //эмулируем ExecutionReport. ждём 100мс, чтобы сделки прошли. а потом получаем их список и пытаемся для 
                                    // добавленных в dictionary заявок выставить проторгованное количество по сделкам.
                                    Thread.Sleep(100);
                                    SendExecutionReportsFromMyLastTrades();
                                }
                                else ErrorOccured?.Invoke(this, new IDaxErrorMessage(0, newOrderIdMessage.Message, ""));

                                preparedOrder = null;
                                preparedOrderParams = null;
                                preparedOrderClientId = "";
                            });
        }

        public void CancelOrder(string clientOrderId, int requestId)
        {
            if (!orderIdsByClientIds.TryGetValue(clientOrderId, out string orderIdToCancel)) orderIdToCancel = clientOrderId;
                //throw new KeyNotFoundException($"ClientOrderId={clientOrderId} was not found in orderIdsByClientIds, " +
                //                               $"which contains these client ids: {string.Join(';', orderIdsByClientIds.Keys)}");

            var cancelResult = DeleteData<RawOrderMessage>("cancelorder", true, new SortedList<string, string> {{"orderId", orderIdToCancel}});
            if (cancelResult.Success)
            {
                orderIdsByClientIds.Remove(clientOrderId, out _);
                OrderCanceled?.Invoke(this, 
                                      cancelResult.Order ?? new IDaxOrderMessage(clientOrderId, 0, "", 0, 0, 0, DateTime.UtcNow.ToString("O")));
            }
            //в качестве description передаём clientOrderId, чтобы удалить её из списка активных
            else ErrorOccured?.Invoke(this, new IDaxErrorMessage((int)RequestError.CancelOrder, cancelResult.Message, clientOrderId));
        }

        public void ReplaceOrder(string oldClientOrderId, string newClientOrderId, decimal price, decimal qty, int requestId)
        {
            throw new NotImplementedException();
        }

        public void GetActiveOrders(int requestId)
        {
            var ordersMessage = GetData<RawActiveOrdersMessage>("myOrders", true, new SortedList<string, string> {{"top", "100"}});
            if (ordersMessage.Success)
            {
                //вроде бессмысленное действие, но оно нужно, чтобы потом при удалении активных заявок найти в dictionary эти id.
                foreach (OrderMessage order in ordersMessage.Orders) orderIdsByClientIds.TryAdd(order.OrderId, order.OrderId);
                ActiveOrdersListArrived?.Invoke(this, ordersMessage.Orders);
            }
            else ErrorOccured?.Invoke(this, new IDaxErrorMessage(0, ordersMessage.Message, ""));
        }

        public void GetPosAndMoney(int requestId)
        {
            var balancesMessage = GetData<RawBalanceMessage>("balances", true, new SortedList<string, string> {{"limit", "500"}});
            if (balancesMessage.Success) BalanceArrived?.Invoke(this, balancesMessage.Balances);
            else ErrorOccured?.Invoke(this, new IDaxErrorMessage(0, balancesMessage.Message, ""));
        }

        void BookTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            pricesTimer.Stop();

            try
            {
                foreach (string isin in isins)
                {
                    (decimal bid, decimal ask) = GetAndSendBook(isin);
                    GetAndSendLast(isin, bid, ask);
                }
            }
            catch (Exception ex)
            {
                ErrorOccured?.Invoke(this, new IDaxErrorMessage(0, ex.Message, ""));
            }
            finally
            {
                pricesTimer.Start();                
            }
        }

        (decimal bid, decimal ask) GetAndSendBook(string isin)
        {
            var book = GetData<IDaxBookMessage>("depth", false, new SortedList<string, string> {{"pair", isin}, {"limit", "5"}});

            if (!book.Success)
            {
                ErrorOccured?.Invoke(this, new IDaxErrorMessage(0, book.Message, "depth"));
                return (0, 0);
            }

            book.SetIsin(isin);
            BookSnapshotArrived?.Invoke(this, book);

            decimal bid = book.Bids.Count > 0 ? book.Bids[0].Price : 0;
            decimal ask = book.Asks.Count > 0 ? book.Asks[0].Price : 0;
            return (bid, ask);
        }

        void GetAndSendLast(string isin, decimal bid, decimal ask)
        {
            var tradesMessage = GetData<RawTradesMessage>("trades", true, new SortedList<string, string> {{"pair", isin}, {"limit", "1"}});
            if (!tradesMessage.Success)
            {
                //TODO: remove when idax fixed
                if (!tradesMessage.Message.Contains("busy")) ErrorOccured?.Invoke(this, new IDaxErrorMessage(0, tradesMessage.Message, "trades"));
                return;
            }

            decimal last = tradesMessage.Trades?.Count > 0 ? tradesMessage.Trades[0].Price : 0;
            var tickerMessage = new IDaxTickerMessage(isin, bid, ask, last);
            TickerArrived?.Invoke(this, tickerMessage);
        }

        void SendExecutionReportsFromMyLastTrades()
        {
            executedOrders.Clear();

            //предполагаем, что заявка исполнилась целиком в одной из 3-х последних сделок
            var tradesMessage = GetData<RawTradesMessage>("myTrades", true, new SortedList<string, string> {{"top", "3"}});
            if (!tradesMessage.Success)
            {
                ErrorOccured?.Invoke(this, new IDaxErrorMessage(0, tradesMessage.Message, "myTrades"));
                return;
            }

            //предполагаем, на этой бирже исин, количество и цена - достаточные поля для идентификации. потому что на время полагаться нельзя.
            //время гуляет +- 30 секунд. при этом своя сделка не содержит номер заявки.
            foreach (IDaxTradeMessage trade in tradesMessage.Trades)
            {
                if (ordersByHash.Remove(trade.StringHash, out IDaxOrderMessage order))
                {
                    //для запомненных новых заявок добавляем проторгованное количество, чтобы правильно отработал ExecutionReport.
                    order.IncreaseTradeQty(trade.Qty);
                    executedOrders.Add(order);
                }

                //IDaxTradeMessage oppositeTrade = trade.CreateOppositeSideTradeMessage();
                //if (ordersByHash.Remove(oppositeTrade.StringHash, out IDaxOrderMessage oppositeOrder))
                //{
                //    order.IncreaseTradeQty(trade.Qty);
                //    executedOrders.Add(oppositeOrder);
                //}
            }

            foreach (IDaxOrderMessage order in executedOrders) ExecutionReportArrived?.Invoke(this, order);
        }

        T GetData<T>(string command, bool needSign, SortedList<string, string> parameters = null)
        {
            return SendRequest<T>("GET", command, needSign, parameters);
        }

        T PostData<T>(string command, bool needSign, SortedList<string, string> parameters = null)
        {
            return SendRequest<T>("POST", command, needSign, parameters);
        }

        T DeleteData<T>(string command, bool needSign, SortedList<string, string> parameters = null)
        {
            return SendRequest<T>("POST", command, needSign, parameters);
        }

        T SendRequest<T>(string method, string command, bool needSign, SortedList<string, string> parameters = null)
        {
            if (parameters == null) parameters = new SortedList<string, string>();

            string jsonString = QueryString(method, command, needSign, parameters);
            T output;

            try
            {
                output = JsonConvert.DeserializeObject<T>(jsonString);
            }
            catch (Exception e)
            {
                e.Data["response"] = jsonString;
                throw;
            }


            return output;
        }

        string QueryString(string method, string relativeUrl, bool needSign, SortedList<string, string> parameters)
        {
            HttpWebRequest request = CreateHttpWebRequest(method, relativeUrl, needSign, parameters);
            return GetResponse(request, relativeUrl, parameters);
        }

        string GetResponse(HttpWebRequest request, string relativeUrl, SortedList<string, string> parameters)
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

                        try
                        {
                            var errorsObject = JsonConvert.DeserializeObject<IDaxErrorMessage>(responseString);
                        }
                        catch (Exception jex)
                        {
                            jex.Data["Request"] = relativeUrl;
                            jex.Data["Response"] = responseString;
                            throw;
                        }

                        foreach (KeyValuePair<string, string> pair in parameters) ex.Data[pair.Key] = pair.Value;
                        throw;
                    }
                }
            }

            using (var sr = new StreamReader(response.GetResponseStream(), encoding))
            {
                string responseString = sr.ReadToEnd();
                return responseString;
            }
        }

        HttpWebRequest CreateHttpWebRequest(string method, string relativeUrl, bool needSign, SortedList<string, string> parameters)
        {
            if (needSign)
            {
                string unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                parameters.Add("key", publicKey);
                parameters.Add("timestamp", unixTimestamp);
            }

            string paramsString = string.Join('&', parameters.Select(pair => $"{pair.Key}={pair.Value}"));

            if (needSign)
            {
                string signature = MakeSignature(paramsString);
                paramsString += $"&sign={signature}";
            }

            HttpWebRequest request = WebRequest.CreateHttp($"{baseUrl}{relativeUrl}?{paramsString}");
            ServicePointManager.SecurityProtocol = Tls | Tls11 | Tls12 | SystemDefault;
            request.Method = method;
            request.Timeout = Timeout.Infinite;

            return request;
        }

        string MakeSignature(string paramsString)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(paramsString);
            byte[] secretKeyBytes = Encoding.UTF8.GetBytes(secretKey);

            byte[] hashBytes;
            using (var encoder = new HMACSHA256(secretKeyBytes))
            {
                hashBytes = encoder.ComputeHash(messageBytes);
            }

            string hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            return hashString;
        }
    }
}
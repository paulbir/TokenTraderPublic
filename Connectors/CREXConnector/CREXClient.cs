using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Timers;
using Combinatorics.Collections;
using CREXConnector.Model;
using Newtonsoft.Json.Linq;
using SharedDataStructures;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using WebSocket4Net;
using ErrorEventArgs = SuperSocket.ClientEngine.ErrorEventArgs;

namespace CREXConnector
{
    public class CREXClient : ITradeConnector
    {
        static readonly string baseUri = "wss://crex.trade/api/ws";

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
        public string ExchangeName => "crex";
        public string PublicKey { get; private set; }

        readonly object wsSendLocker = new object();
        readonly ConcurrentDictionary<int, RequestError> possibleErrorByRequestId = new ConcurrentDictionary<int, RequestError>();     
        readonly ConcurrentDictionary<string, bool> gotActiveOrdersForIsin = new ConcurrentDictionary<string, bool>();
        readonly List<CREXOrderMessage> allActiveOrders = new List<CREXOrderMessage>();
        readonly ConcurrentDictionary<string, string> requestedIsinByRequestId = new ConcurrentDictionary<string, string>();
        readonly ConcurrentDictionary<string, CREXOrderMessage> ordersByClientId = new ConcurrentDictionary<string, CREXOrderMessage>();
        readonly ConcurrentDictionary<long, CREXOrderMessage> ordersByExchangeId = new ConcurrentDictionary<long, CREXOrderMessage>();
        readonly ConcurrentDictionary<long, List<CREXTrade>> tradesByOrderId = new ConcurrentDictionary<long, List<CREXTrade>>();

        //readonly StreamWriter sw = new StreamWriter("json_messages.txt") {AutoFlush = true};

        List<string> isins;
        string publicKey;
        string secretKey;

        Timer timeoutTimer;
        Timer sessionExpirationTimer;
        WebSocket ws;

        string refreshToken;
        HashSet<string> possibleTradableIsins;
        HashSet<string> allCREXIsins;
        string preparedOrder = "";

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
            ws = new WebSocket(baseUri) { EnableAutoSendPing = true, AutoSendPingInterval = 1000 };

            ws.Opened += Ws_Opened;
            ws.Closed += Ws_Closed;
            ws.Error += Ws_Error;
            ws.MessageReceived += Ws_MessageReceived;
            ws.Open();
        }

        public void Stop()
        {
            ClearOnStop();
            ws?.Close();
        }

        public void AddOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            string sideStr = side == OrderSide.Buy ? "BUY" : "SELL";
            string priceStr = price.ToString(CultureInfo.InvariantCulture);
            string qtyStr = qty.ToString(CultureInfo.InvariantCulture);
            string priceCurrency = isin.Split('_')[1];


            string placeLimitOrderMessage =
                MessageCreator.CreatePlaceLimitOrderMessage(clientOrderId, isin, priceStr, qtyStr, priceCurrency, sideStr, requestId);
            possibleErrorByRequestId.TryAdd(requestId, RequestError.AddOrder);
            WsThreadSafeSend(placeLimitOrderMessage);
        }

        public void PrepareOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            string sideStr = side == OrderSide.Buy ? "BUY" : "SELL";
            string priceStr = price.ToString(CultureInfo.InvariantCulture);
            string qtyStr = qty.ToString(CultureInfo.InvariantCulture);
            string priceCurrency = isin.Split('_')[1];

            preparedOrder = MessageCreator.CreatePlaceLimitOrderMessage(clientOrderId, isin, priceStr, qtyStr, priceCurrency, sideStr, requestId);
        }

        public Task SendPreparedOrder()
        {
            if (preparedOrder != "")
            {
                WsThreadSafeSend(preparedOrder);
                preparedOrder = "";
            }

            return Task.CompletedTask;
        }

        public void CancelOrder(string clientOrderId, int requestId)
        {
            if (!ordersByClientId.TryGetValue(clientOrderId, out CREXOrderMessage order))
            {
                ErrorOccured?.Invoke(this,
                                     new CREXErrorMessage((int)RequestError.CancelOrder,
                                                          $"ClientOderId {clientOrderId} was not found in ordersByClientId.",
                                                          ""));
                return;
            }

            string killOrderMessage = MessageCreator.CreateKillOrderMessage(order.ExchangeOrderId, clientOrderId, order.Isin, requestId);
            possibleErrorByRequestId.TryAdd(requestId, RequestError.CancelOrder);
            WsThreadSafeSend(killOrderMessage);
        }

        public void ReplaceOrder(string oldClientOrderId, string newClientOrderId, decimal price, decimal qty, int requestId)
        {
            throw new NotImplementedException();
        }

        public void GetActiveOrders(int requestId)
        {
            possibleErrorByRequestId.TryAdd(requestId, RequestError.ActiveOrders);
            int i = 0;
            foreach (string isin in possibleTradableIsins)
            {
                string requestIdStr = $"{requestId}_{i++}";
                requestedIsinByRequestId.TryAdd(requestIdStr, isin);
                string activeOrdersRequestMessage = MessageCreator.CreateGetActiveOrdersRequestMessage(isin, requestIdStr);
                WsThreadSafeSend(activeOrdersRequestMessage);
            }
        }

        public void GetPosAndMoney(int requestId)
        {
            string getPositionsRequestMessage = MessageCreator.CreateGetPositionsRequestMessage(requestId);
            possibleErrorByRequestId.TryAdd(requestId, RequestError.TradingBalance);
            WsThreadSafeSend(getPositionsRequestMessage);
        }

        void TimeoutTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            timeoutTimer?.Stop();
            Disconnected?.Invoke(this, null);
        }

        void SessionExpirationTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (string.IsNullOrEmpty(refreshToken))
            {
                ErrorOccured?.Invoke(this, new CREXErrorMessage(0, "RefreshToken is not set", ""));
                return;
            }

            string renewTokenRequestMessage = MessageCreator.CreateRenewTokenRequestMessage(refreshToken);
            WsThreadSafeSend(renewTokenRequestMessage);
        }

        void Ws_Opened(object sender, EventArgs e)
        {

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
            //sw.WriteLine($"IN|{DateTime.UtcNow:u};{e.Message}");

            JObject messageObject = JObject.Parse(e.Message);
            string type = (string)messageObject.SelectToken("_type");
            switch (type)
            {
                case "orderbook_update":
                    timeoutTimer?.Stop();
                    ProcessBookUpdate(messageObject);
                    timeoutTimer?.Start();
                    break;

                case "hello":
                    ProcessHello();
                    break;

                case "new_auth_session":
                    ProcessNewAuthSession(messageObject);
                    break;

                case "auth_ok":
                    ProcessAuthOk(messageObject);
                    break;

                case "logged_in":
                    ProcessLoginResponse();
                    break;

                case "token_renewed":
                    ProcessRenewTokenResponse(messageObject);
                    break;

                case "orderbook":
                    ProcessBookSnapshot(messageObject);
                    break;

                case "positions":
                    ProcessPositions(messageObject);
                    break;

                case "orders":
                    ProcessOrders(messageObject);
                    break;

                case "ticker_update":
                    timeoutTimer?.Stop();
                    ProcessTicker(messageObject);
                    timeoutTimer?.Start();
                    break;

                case "instruments":
                    ProcessInstruments(messageObject);
                    break;

                case "order_update":
                    ProcessOrderUpdate(messageObject);
                    break;

                case "trade_update":
                    ProcessTradeUpdate(messageObject);
                    break;

                case "transaction_update":
                    ProcessTransactionUpdate(messageObject);
                    break;

                case "error":
                    ProcessErrorMessage(messageObject);
                    break;
            }
        }

        void ProcessHello()
        {
            string beginAuthRequestMessage = MessageCreator.CreateBeginAuthRequestMessage();
            WsThreadSafeSend(beginAuthRequestMessage);
        }

        void ProcessNewAuthSession(JObject messageObject)
        {
            string sessionId = (string)messageObject.SelectToken("session_id");

            string authPasswordRequestMessage = MessageCreator.CreateAuthPasswordRequestMessage(sessionId, publicKey, secretKey);
            WsThreadSafeSend(authPasswordRequestMessage);
        }

        void ProcessAuthOk(JObject messageObject)
        {
            ProcessAuthMessage(messageObject, out string accessToken);

            string loginRequestMessage = MessageCreator.CreateLoginRequestMessage(accessToken);
            WsThreadSafeSend(loginRequestMessage);
        }

        void ProcessLoginResponse()
        {
            string getInstrumentsRequestMessage = MessageCreator.CreateGetInstrumentsRequestMessage();
            WsThreadSafeSend(getInstrumentsRequestMessage);

            string getPositionsRequestMessage = MessageCreator.CreateGetPositionsRequestMessage(-1);
            possibleErrorByRequestId.TryAdd(-1, RequestError.TradingBalance);
            WsThreadSafeSend(getPositionsRequestMessage);

            if (isins != null && isins.Count > 0)
            {
                foreach (string isin in isins)
                {
                    string orderBookSnapshotRequestMessage = MessageCreator.CreateOrderBookSnapshotRequestMessage(isin);
                    WsThreadSafeSend(orderBookSnapshotRequestMessage);

                    string orderBookSubscriptionRequestMessage = MessageCreator.CreateOrderBookSubscriptionRequestMessage(isin);
                    WsThreadSafeSend(orderBookSubscriptionRequestMessage);

                    string tickerSubscriptionRequestMessage = MessageCreator.CreateTickerSubscriptionRequestMessage(isin);
                    WsThreadSafeSend(tickerSubscriptionRequestMessage);
                }

                timeoutTimer?.Start();
            }
        }

        void ProcessRenewTokenResponse(JObject messageObject)
        {
            ProcessAuthMessage(messageObject, out string accessToken);

            string renewLoginRequestMessage = MessageCreator.CreateRenewLoginRequestMessage(accessToken);
            WsThreadSafeSend(renewLoginRequestMessage);
        }

        void ProcessAuthMessage(JObject messageObject, out string accessToken)
        {
            accessToken = (string)messageObject.SelectToken("access_token");
            refreshToken = (string)messageObject.SelectToken("refresh_token");
            int sessionExpirationSec = (int)messageObject.SelectToken("expires_in");

            //уменьшаем, чтобы обновлять accessToken не впритык к моменту окончания сессии.
            //если времени много, то просто вычтем 10 секунд. в другом случае поделим на 2, чтобы < 0 не получилось.
            sessionExpirationSec = sessionExpirationSec > 60 ? sessionExpirationSec - 10 : sessionExpirationSec / 2;
            sessionExpirationTimer?.Stop();
            sessionExpirationTimer = new Timer(sessionExpirationSec * 1000);
            sessionExpirationTimer.Elapsed += SessionExpirationTimer_Elapsed;
            sessionExpirationTimer.Start();
        }

        void ProcessBookSnapshot(JObject messageObject)
        {
            var snapshot = messageObject.ToObject<CREXBookSnapshotMessage>();
            BookSnapshotArrived?.Invoke(this, snapshot);
        }

        void ProcessBookUpdate(JObject messageObject)
        {
            var update = messageObject.ToObject<CREXBookUpdateMessage>();
            BookUpdateArrived?.Invoke(this, update);
        }

        void ProcessPositions(JObject messageObject)
        {
            JToken items = messageObject.SelectToken("items");
            List<BalanceMessage> balances = items.ToObject<List<CREXBalanceMessage>>().Select(balance => (BalanceMessage)balance).ToList();

            if (balances.Count == 0)
            {
                //если possibleTradableIsins == null, то это получение балансов во время инициализации для составления списка возможных торгуемых исинов.
                //если получили null, то инициализация провалилась.
                if (possibleTradableIsins == null)
                    throw new RequestFailedException($"GetPositions request returned 0 balances during " + 
                                                     $"initialization for connector {Name} and publicKey={publicKey}");

                ErrorOccured?.Invoke(this, new CREXErrorMessage((int)RequestError.TradingBalance, "Received balances message with 0 balances.", ""));
                return;
            }

            TryRemoveRequestIdFromPossibleErrors(messageObject, out _);

            if (possibleTradableIsins == null) //инициализация
            {
                SetPossibleTradableIsins(balances);

                SubscribeToOrders();
                SubscribeToTrades();
                SubscribeToTransactions();

                Connected?.Invoke(this, null); //здесь оканчивается инициализация
                return;
            }

            BalanceArrived?.Invoke(this, balances);
        }

        void ProcessOrders(JObject messageObject)
        {
            JToken items = messageObject.SelectToken("items");
            JToken requestIdToken = messageObject.SelectToken("req_id");
            string requestIdStr = requestIdToken != null ? (string)requestIdToken : "";

            //так как для получаения активных заявок нужно обязательно указать исин, то приходится перебирать все исины по очереди.
            //для того, чтобы узнать про какой исин сейчас пришло сообщение (вдруг заявок нет и из заявки нельзя взять исин), сделан такой костыль:
            // словарь исинов по requestId.
            requestedIsinByRequestId.TryRemove(requestIdStr, out string isin);

            var activeOrdersOneIsin = items.ToObject<List<CREXOrderMessage>>();
            allActiveOrders.AddRange(activeOrdersOneIsin);

            //исин должны получить либо из словаря, либо из первой попавшейся активной заявки. иначе ошибка
            if (string.IsNullOrEmpty(isin))
            {
                if (activeOrdersOneIsin.Count > 0) isin = activeOrdersOneIsin.First().Isin;
                else
                {
                    ErrorOccured?.Invoke(this,
                                         new CREXErrorMessage((int)RequestError.ActiveOrders,
                                                              $"RequestId={requestIdStr} was not found in requestedIsinByRequestId and " +
                                                              $"got no active orders to extract isin.",
                                                              ""));
                }
            }
            gotActiveOrdersForIsin[isin] = true;

            //получили все сообщения для всех запрошенных исинов
            if (gotActiveOrdersForIsin.Values.All(value => value))
            {
                foreach (string isinLoop in gotActiveOrdersForIsin.Keys.ToList()) gotActiveOrdersForIsin[isinLoop] = false;
                int requestId = Convert.ToInt32(requestIdStr.Split('_')[0]);
                possibleErrorByRequestId.TryRemove(requestId, out _);
                foreach (CREXOrderMessage order in allActiveOrders)
                {
                    ordersByClientId.TryAdd(order.OrderId, order);
                    ordersByExchangeId.TryAdd(order.ExchangeOrderId, order);
                }
                ActiveOrdersListArrived?.Invoke(this, allActiveOrders.Select(order => (OrderMessage)order).ToList());
                allActiveOrders.Clear();
            }
        }

        void ProcessTicker(JObject messageObject)
        {
            JToken tickerToken = messageObject.SelectToken("item");
            if (tickerToken == null) return;

            var ticker = tickerToken.ToObject<CREXTickerMessage>();
            TickerArrived?.Invoke(this, ticker);
        }

        void ProcessInstruments(JObject messageObject)
        {
            JToken items = messageObject.SelectToken("items");
            if (items == null)
                throw new RequestFailedException($"GetInstruments request returned null instruments for connector {Name} and publicKey={publicKey}");

            var instruments = items.ToObject<List<CREXInstrument>>();
            if (instruments.Count == 0)
                throw new RequestFailedException($"GetInstruments request returned 0 instruments for connector {Name} and publicKey={publicKey}");

            allCREXIsins = instruments.Select(instrument => instrument.Isin).ToHashSet();
        }

        void ProcessOrderUpdate(JObject messageObject)
        {
            var order = messageObject.SelectToken("item").ToObject<CREXOrderMessage>();

            switch (order.Status)
            {
                case "ACTIVE":
                case "FILLED":
                    //заявка новая. в словарях пока нет
                    if (!ordersByClientId.TryGetValue(order.OrderId, out CREXOrderMessage savedOrder) && !ordersByExchangeId.TryGetValue(order.ExchangeOrderId, out savedOrder))
                    {
                        ordersByClientId.TryAdd(order.OrderId, order);
                        ordersByExchangeId.TryAdd(order.ExchangeOrderId, order);
                        NewOrderAdded?.Invoke(this, order);
                    }
                    else savedOrder.UpdateStatus(order.Status);

                    //если сделки пришли раньше, то они добавились в буфер
                    ApplyTradesFromBuffer(order);
                    
                    break;

                case "CANCELED":
                    ordersByClientId.TryRemove(order.OrderId, out _);
                    ordersByExchangeId.TryRemove(order.ExchangeOrderId, out _);
                    OrderCanceled?.Invoke(this, order);
                    break;

                case "REJECTED":
                    ErrorOccured?.Invoke(this, new CREXErrorMessage((int)RequestError.AddOrder, "Order rejected.", order.OrderId));
                    break;
            }

            TryRemoveRequestIdFromPossibleErrors(messageObject, out _);
        }

        void ProcessTradeUpdate(JObject messageObject)
        {
            var trade = messageObject.SelectToken("item").ToObject<CREXTrade>();

            //когда сделка пришла раньше заявки, откладываем её в буфер
            if (!ordersByExchangeId.TryGetValue(trade.ExchangeOrderId, out CREXOrderMessage order))
            {
                if (!tradesByOrderId.TryGetValue(trade.ExchangeOrderId, out List<CREXTrade> tradesList))
                {
                    tradesList = new List<CREXTrade>();
                    tradesByOrderId.TryAdd(trade.ExchangeOrderId, tradesList);
                }

                tradesList.Add(trade);
                return;
            }

            //в противном случае сообщение о заявке уже было
            order.DecreaseQtyLeftOnTrade(trade.Qty);
            if (order.QtyLeft <= 0 && order.Status == "FILLED") //заявку целиком забрали
            {
                ordersByClientId.TryRemove(order.OrderId, out _);
                ordersByExchangeId.TryRemove(order.ExchangeOrderId, out _);
            }

            CREXOrderMessage executionReport = order.DuplicateWithNewTrade(trade.Qty, trade.Fee);
            ExecutionReportArrived?.Invoke(this, executionReport);
        }

        void ProcessTransactionUpdate(JObject messageObject)
        {
            var transaction = messageObject.SelectToken("item").ToObject<CREXTransaction>();

            if (transaction.Status == "FAILED" || transaction.Status == "REJECTED")
                ErrorOccured?.Invoke(this,
                                     new CREXErrorMessage((int)RequestError.AddOrder,
                                                          $"Order transaction {transaction.Status}. Message: {transaction.Message}.",
                                                          transaction.ClientTransactionId));
        }

        void ProcessErrorMessage(JObject messageObject)
        {
            string message = (string)messageObject.SelectToken("error");
            string description = (string)messageObject.SelectToken("msg");
            TryRemoveRequestIdFromPossibleErrors(messageObject, out RequestError code);

            ErrorOccured?.Invoke(this, new CREXErrorMessage((int)code, message, description));
        }

        void ClearOnStop()
        {
            timeoutTimer?.Stop();
            sessionExpirationTimer?.Stop();

            possibleErrorByRequestId.Clear();
            gotActiveOrdersForIsin.Clear();
            allActiveOrders.Clear();
            requestedIsinByRequestId.Clear();
            ordersByClientId.Clear();
            ordersByExchangeId.Clear();
            tradesByOrderId?.Clear();

            refreshToken = null;
            possibleTradableIsins = null;
            allCREXIsins = null;
        }

        void ApplyTradesFromBuffer(CREXOrderMessage order)
        {
            if (tradesByOrderId.TryRemove(order.ExchangeOrderId, out List<CREXTrade> trades)) //сразу удаляем
            {
                //отправляем экзекьюшен репорты
                foreach (CREXTrade trade in trades)
                {
                    order.DecreaseQtyLeftOnTrade(trade.Qty);
                    CREXOrderMessage executionReport = order.DuplicateWithNewTrade(trade.Qty, trade.Fee);
                    ExecutionReportArrived?.Invoke(this, executionReport);
                }

                if (order.QtyLeft <= 0 && order.Status == "FILLED") //заявку целиком забрали
                {
                    ordersByClientId.TryRemove(order.OrderId, out _);
                    ordersByExchangeId.TryRemove(order.ExchangeOrderId, out _);
                }
            }
        }

        void TryRemoveRequestIdFromPossibleErrors(JObject messageObject, out RequestError errorCode)
        {
            JToken requestIdToken = messageObject.SelectToken("req_id");
            int requestId = requestIdToken != null ? (int)requestIdToken : 0;
            possibleErrorByRequestId.TryRemove(requestId, out errorCode);
        }

        void SetPossibleTradableIsins(List<BalanceMessage> balances)
        {
            List<BalanceMessage> positiveBalances = balances.Where(balance => balance.Available + balance.Reserved > 0).ToList();
            var balanceCombinations = new Combinations<string>(positiveBalances.Select(balance => balance.Currency).ToList(), 2);

            possibleTradableIsins = new HashSet<string>();
            foreach (IList<string> combination in balanceCombinations)
            {
                possibleTradableIsins.UnionWith(allCREXIsins.Where(isin => isin.Contains(combination[0]) && isin.Contains(combination[1])));
            }

            gotActiveOrdersForIsin.Clear();
            foreach (string isin in possibleTradableIsins) gotActiveOrdersForIsin.TryAdd(isin, false);
        }

        void SubscribeToOrders()
        {
            foreach (string isin in possibleTradableIsins)
            {
                string orderSubscriptionRequestMessage = MessageCreator.CreateOrderSubscriptionRequestMessage(isin);
                WsThreadSafeSend(orderSubscriptionRequestMessage);
            }
        }

        void SubscribeToTrades()
        {
            foreach (string isin in possibleTradableIsins)
            {
                string tradeSubscriptionRequestMessage = MessageCreator.CreateTradeSubscriptionRequestMessage(isin);
                WsThreadSafeSend(tradeSubscriptionRequestMessage);
            }
        }

        void SubscribeToTransactions()
        {
            foreach (string isin in possibleTradableIsins)
            {
                string transactionsSubscriptionRequestMessage = MessageCreator.CreateTransactionsSubscriptionRequestMessage(isin);
                WsThreadSafeSend(transactionsSubscriptionRequestMessage);
            }
        }

        void WsThreadSafeSend(string message)
        {
            lock (wsSendLocker)
            {
                //sw.WriteLine($"OUT|{DateTime.UtcNow:u};{message}");
                ws?.Send(message);
            }
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharedDataStructures;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SuperSocket.ClientEngine;
using WebSocket4Net;
using XenaConnector.Model;

namespace XenaConnector
{
    public class XenaClient : ITradeConnector
    {
        //readonly string wsBaseUri = "ws://104.199.4.174";
        //readonly string restBaseUri = "http://104.199.4.174";
        readonly string wsBaseUri = "wss://xena.exchange";
        readonly string marketDataEndpoint = "/api/ws/market-data";
        readonly string tadingEndpoint = "/api/ws/trading";
        readonly int minMarginalAccountId = 1012833458;
        readonly bool isMarginMarket;

        List<string> isins;
        string publicKey;
        string secretKey;

        //readonly ConcurrentDictionary<string, XenaBalanceMessage> balanceByCurrency = new ConcurrentDictionary<string, XenaBalanceMessage>();
        readonly ConcurrentDictionary<string, string> isinByOrderId = new ConcurrentDictionary<string, string>();
        readonly ConcurrentDictionary<string, XenaPositionMessage> positionByIsin = new ConcurrentDictionary<string, XenaPositionMessage>();
        readonly object tradingWsSendLocker = new object();

        Timer timeoutTimer;
        Timer heartbeatTimer;
        WebSocket tradingWs;
        WebSocket marketDataWs;

        int tradingAccountId;
        int hearbeatIntervalMs;
        bool gotMarketDataLogon;
        bool gotTradingLogon;
        string preparedOrder = "";

        public XenaClient(ITradeConnectorContext context)
        {
            if (context == null) return;
            isMarginMarket = context.IsMarginMarket;
        }

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
        public string ExchangeName => "xena";
        public string PublicKey { get; private set; }

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
            //открываем, если сокет не создан или не подключен. на случай, когда надо переподключиться.
            if (isins?.Count > 0 && (marketDataWs == null || marketDataWs.State != WebSocketState.Open))
            {
                //Console.WriteLine("starting md ws");
                marketDataWs = new WebSocket(wsBaseUri + marketDataEndpoint) {EnableAutoSendPing = true, AutoSendPingInterval = 1000};
                marketDataWs.Opened += MarketDataWs_Opened;
                marketDataWs.Closed += Ws_Closed;
                marketDataWs.Error += Ws_Error;
                marketDataWs.MessageReceived += Ws_MessageReceived;
                marketDataWs.Open();
            }

            //если оба ключа null или empty, то это значит, что не торгуем, а только получаем данные. торговый сокет не подключаем.
            //открываем, если сокет не создан или не подключен. на случай, когда надо переподключиться.
            if ((!string.IsNullOrEmpty(publicKey) || !string.IsNullOrEmpty(secretKey)) && (tradingWs == null || tradingWs.State != WebSocketState.Open))
            {
                //Console.WriteLine("starting trade ws");
                tradingWs = new WebSocket(wsBaseUri + tadingEndpoint) {EnableAutoSendPing = true, AutoSendPingInterval = 1000};
                tradingWs.Opened += TradingWs_Opened;
                tradingWs.Closed += Ws_Closed;
                tradingWs.Error += Ws_Error;
                tradingWs.MessageReceived += Ws_MessageReceived;
                tradingWs.Open();
            }
        }

        public void Stop()
        {
            timeoutTimer?.Stop();
            heartbeatTimer?.Stop();
            marketDataWs?.Close();
            tradingWs?.Close();

            gotMarketDataLogon = false;
            gotTradingLogon = false;
            hearbeatIntervalMs = 0;
        }

        public void AddOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            string addOrderMessage = PrepareOrderMessage(clientOrderId, isin, side, price, qty);
            TradingWsThreadSafeSend(addOrderMessage);
        }

        public void PrepareOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            preparedOrder = PrepareOrderMessage(clientOrderId, isin, side, price, qty);
        }

        public Task SendPreparedOrder()
        {
            if (preparedOrder != "")
            {
                TradingWsThreadSafeSend(preparedOrder);
                preparedOrder = "";
            }

            return Task.CompletedTask;
        }

        public void CancelOrder(string clientOrderId, int requestId)
        {
            if (!isinByOrderId.TryGetValue(clientOrderId, out string isin))
            {
                ErrorOccured?.Invoke(this, new XenaErrorMessage((int)RequestError.CancelOrder, "isinByOrderId doesn't contain clientOrderId", clientOrderId));
                return;
            }

            string cancelOrderMessage = MessageCreator.CreateCancelOrderMessage(clientOrderId, isin, tradingAccountId);
            TradingWsThreadSafeSend(cancelOrderMessage);
        }

        public void ReplaceOrder(string oldClientOrderId, string newClientOrderId, decimal price, decimal qty, int requestId)
        {
            throw new NotImplementedException();
        }

        public void GetActiveOrders(int requestId)
        {
            string ordersMassStatusRequestMessage = MessageCreator.CreateOrdersMassStatusRequestMessage(tradingAccountId);
            TradingWsThreadSafeSend(ordersMassStatusRequestMessage);
        }

        public void GetPosAndMoney(int requestId)
        {
            //List<BalanceMessage> balances = balanceByCurrency.Values.Select(balance => (BalanceMessage)balance).ToList();
            //BalanceArrived?.Invoke(this, balances);
            string accountStatusReportRequestMessage = MessageCreator.CreateAccountStatusReportRequestMessage(tradingAccountId);
            TradingWsThreadSafeSend(accountStatusReportRequestMessage);

            string positionReportRequestMessage = MessageCreator.CreatePositionReportRequestMessage(tradingAccountId);
            TradingWsThreadSafeSend(positionReportRequestMessage);
        }

        string PrepareOrderMessage(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty)
        {
            string sideStr = side == OrderSide.Buy ? "1" : "2";
            string priceStr = price.ToString(CultureInfo.InvariantCulture);
            string qtyStr = qty.ToString(CultureInfo.InvariantCulture);
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000_000;

            bool isMarginCloseMode = false;
            int positionId = 0;
            //if (isMarginMarket && positionByIsin.TryGetValue(isin, out XenaPositionMessage position))
            //{
            //    if (side == OrderSide.Buy && position.Qty < 0 || side == OrderSide.Sell && position.Qty > 0)
            //    {
            //        isMarginCloseMode = true;
            //        positionId = position.PositionId;
            //    }
            //}

            return MessageCreator.CreateAddOrderMessage(clientOrderId,
                                                        isin,
                                                        sideStr,
                                                        priceStr,
                                                        qtyStr,
                                                        tradingAccountId,
                                                        timestamp,
                                                        positionId,
                                                        isMarginMarket,
                                                        isMarginCloseMode);
        }

        void TimeoutTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            //Console.WriteLine("md timeout");
            timeoutTimer?.Stop();
            heartbeatTimer?.Stop();

            gotMarketDataLogon = false;
            gotTradingLogon = false;
            hearbeatIntervalMs = 0;

            Disconnected?.Invoke(this, null);
        }

        void HeartbeatTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            string heartbeatMessage = MessageCreator.CreateHeartbeatMessage();

            marketDataWs?.Send(heartbeatMessage);
            TradingWsThreadSafeSend(heartbeatMessage);
        }

        void MarketDataWs_Opened(object sender, EventArgs e)
        {
            if (isins == null || isins.Count == 0) return;
            timeoutTimer?.Start();

            // только данные. не торгуем. поэтому считаем, что подключились, потому что только этот сокет работает.
            if (tradingWs == null) Connected?.Invoke(this, null);

            foreach (string isin in isins)
            {
                string subscribeBookMessage = MessageCreator.CreateSubscribeToBookMessage(isin);
                string subscribeTradesMessage = MessageCreator.CreateSubscribeToMarketTrades(isin);
                marketDataWs.Send(subscribeBookMessage);
                marketDataWs.Send(subscribeTradesMessage);
            }
        }

        void TradingWs_Opened(object sender, EventArgs e)
        {
            long nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000_000;
            string payload = $"AUTH{nonce}";
            string signature = XENASignature.Sign(secretKey, payload);
            string logonMessage = MessageCreator.CreateLogonMessage(publicKey, nonce, payload, signature);
            TradingWsThreadSafeSend(logonMessage);
        }

        void Ws_Closed(object sender, EventArgs e)
        {
            var ws = (WebSocket)sender;

            string socket;
            if (ws == marketDataWs) socket = "md";
            else if (ws == tradingWs) socket = "trade";
            else socket = "unknown";
            //Console.WriteLine($"{DateTime.UtcNow} {socket} disconnected.");

            timeoutTimer?.Stop();
            heartbeatTimer?.Stop();

            gotMarketDataLogon = false;
            gotTradingLogon = false;
            hearbeatIntervalMs = 0;

            Disconnected?.Invoke(this, null);
        }

        void Ws_Error(object sender, ErrorEventArgs e)
        {
            throw e.Exception;
        }

        void Ws_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            string message = e.Message;
            JObject messageObject = JObject.Parse(message);
            string msgType = (string)messageObject.SelectToken(Tags.MsgType);
            switch (msgType)
            {
                case MsgTypes.MktDataUpdate:
                    timeoutTimer?.Stop();

                    string streamId = (string)messageObject.SelectToken(Tags.MDStreamID);
                    if (streamId.StartsWith("DOM")) ProcessUpdate(messageObject);
                    else if (streamId.StartsWith("trades")) ProcessMarketTrades(messageObject);

                    timeoutTimer?.Start();
                    break;

                case MsgTypes.MktDataSnapshot:
                    streamId = (string)messageObject.SelectToken(Tags.MDStreamID);
                    if (streamId.StartsWith("DOM")) ProcessSnapshot(messageObject);
                    break;

                case MsgTypes.Logon:
                    var ws = (WebSocket)sender;
                    ProcessLogon(messageObject, ws);
                    break;

                case MsgTypes.ExecutionReport:
                    ProcessExecutionReport(messageObject);
                    break;

                case MsgTypes.AccountStatusSnapshot:
                    ProcessBalanceSnapshot(messageObject);
                    break;

                case MsgTypes.OrderMassStatusResponse:
                    ProcessOrdersMassStatus(messageObject);
                    break;

                case MsgTypes.OrderCancelReject:
                    ProcessOrderCancelReject(messageObject);
                    break;

                case MsgTypes.Reject:
                    ProcessReject(messageObject);
                    break;

                case MsgTypes.MassPositionReport:
                    ProcessPositionReport(messageObject);
                    break;
            }
        }

        void ProcessLogon(JObject messageObject, WebSocket ws)
        {
            if (ws == marketDataWs) ProcessMarketLogon(messageObject);
            else if (ws == tradingWs) ProcessTradingLogon(messageObject);

            //если получили логоны по всем открытым сокетам то включаем хартбиты и запускаем таймер для данных, если надо получать.
            if (GotAllNeccessaryLogons())
            {
                if (isins?.Count > 0)
                {
                    //Console.WriteLine("stating md timeout timer");
                    timeoutTimer?.Start();
                }

                heartbeatTimer = new Timer(hearbeatIntervalMs);
                heartbeatTimer.Elapsed += HeartbeatTimer_Elapsed;
                heartbeatTimer.Start();
            }
        }

        void ProcessMarketLogon(JObject messageObject)
        {
            //Console.WriteLine("processing md logon");
            int logonHeartbeatIntervalSec = (int)messageObject.SelectToken(Tags.HeartBtInt);
            if (hearbeatIntervalMs == 0 || logonHeartbeatIntervalSec * 1000 / 2 < hearbeatIntervalMs)
                hearbeatIntervalMs = logonHeartbeatIntervalSec * 1000 / 2;

            gotMarketDataLogon = true;
        }

        void ProcessTradingLogon(JObject messageObject)
        {
            //Console.WriteLine("processing trade logon");
            var logon = messageObject.ToObject<TradingLogon>();
            if (logon.SessionStatus != null && logon.SessionStatus != "0" || logon.RejectText != null)
            {
                var ex = new RequestFailedException($"Logon to trading websocket failed for connector {Name} with publicKey={publicKey}.");
                ex.Data["SessionStatus"] = logon.SessionStatus;
                ex.Data["RejectText"] = logon.RejectText;
                throw ex;
            }

            SetAccounts(logon);

            if (hearbeatIntervalMs == 0 || logon.HeartbeatIntervalSec * 1000 / 2 < hearbeatIntervalMs)
                hearbeatIntervalMs = logon.HeartbeatIntervalSec * 1000 / 2;

            gotTradingLogon = true;

            Connected?.Invoke(this, null);
        }

        void SetAccounts(TradingLogon logon)
        {
            if (logon.Accounts == null || logon.Accounts.Count == 0 || logon.Accounts.Count > 2 ||
                logon.Accounts.Count(account => account < minMarginalAccountId) > 1 || logon.Accounts.Count(account => account >= minMarginalAccountId) > 1)
            {
                string accountsState = logon.Accounts == null ? "null Accounts" : $"{logon.Accounts.Count}: {string.Join(';', logon.Accounts)}";
                throw new RequestFailedException($"Trading logon request returned {accountsState} for connector {Name} with publicKey={publicKey}.");
            }

            tradingAccountId = isMarginMarket
                                   ? logon.Accounts.Single(account => account >= minMarginalAccountId)
                                   : logon.Accounts.Single(account => account < minMarginalAccountId);
        }

        bool GotAllNeccessaryLogons()
        {
            //подразумевается, что хотя бы один сокет не нулл.
            bool result = tradingWs == null || gotTradingLogon;
            result &= marketDataWs == null || gotMarketDataLogon;

            return result;
        }

        void ProcessSnapshot(JObject messageObject)
        {
            var snapshot = messageObject.ToObject<XenaBookMessage>();
            snapshot.SetBase();
            BookSnapshotArrived?.Invoke(this, snapshot);
        }

        void ProcessUpdate(JObject messageObject)
        {
            var update = messageObject.ToObject<XenaBookMessage>();
            update.SetBase();
            BookUpdateArrived?.Invoke(this, update);
        }

        void ProcessMarketTrades(JObject messageObject)
        {
            var trades = messageObject.ToObject<MarketTradesMessage>();
            decimal last = trades.Last;
            var tickerMessage = new XenaTickerMessage(trades.Isin, -1, -1, last);
            TickerArrived?.Invoke(this, tickerMessage);
        }

        void ProcessExecutionReport(JObject messageObject)
        {
            var order = messageObject.ToObject<XenaOrderMessage>();
            if (order.AccountId != tradingAccountId) return;
            order.SetBase();

            switch (order.EnumStatus)
            {
                case XenaOrderStatuses.New:
                    if (order.EnumExecType != XenaExecTypes.New) return;
                    isinByOrderId.TryAdd(order.OrderId, order.Isin);
                    NewOrderAdded?.Invoke(this, order);
                    break;

                case XenaOrderStatuses.PartiallyFilled:
                    ExecutionReportArrived?.Invoke(this, order);
                    TryCollapseAndGetPositions(order.Isin);
                    break;

                case XenaOrderStatuses.Filled:
                    isinByOrderId.TryRemove(order.OrderId, out _);
                    ExecutionReportArrived?.Invoke(this, order);
                    TryCollapseAndGetPositions(order.Isin);
                    break;

                case XenaOrderStatuses.Cancelled:
                    isinByOrderId.TryRemove(order.OrderId, out _);
                    OrderCanceled?.Invoke(this, order);
                    break;

                case XenaOrderStatuses.Rejected:
                case XenaOrderStatuses.Unknown:
                    ErrorOccured?.Invoke(this,
                                         new XenaErrorMessage((int)RequestError.AddOrder,
                                                              $"New order rejected or has unknown status {order.Status}",
                                                              order.RejectText));
                    break;
            }

            void TryCollapseAndGetPositions(string isin)
            {
                if (!isMarginMarket) return;
                string positionMaintenanceRequestMessage = MessageCreator.CreatePositionMaintenanceRequestMessage(tradingAccountId, isin);
                TradingWsThreadSafeSend(positionMaintenanceRequestMessage);

                string positionReportRequestMessage = MessageCreator.CreatePositionReportRequestMessage(tradingAccountId);
                TradingWsThreadSafeSend(positionReportRequestMessage);
            }
        }

        void ProcessBalanceSnapshot(JObject messageObject)
        {
            var rawBalances = messageObject.ToObject<RawBalancesMessage>();

            if (rawBalances.Account != tradingAccountId || rawBalances.Balances == null) return;

            //foreach (RawBalance rawBalance in rawBalances.Balances)
            //    balanceByCurrency.TryAdd(rawBalance.Currency, new XenaBalanceMessage(rawBalance.Currency, rawBalance.Available, rawBalance.Reserved));

            List<BalanceMessage> balances =
                rawBalances.Balances.
                            Select(rawBalance => (BalanceMessage)new XenaBalanceMessage(rawBalance.Currency, rawBalance.Available, rawBalance.Reserved)).
                            ToList();

            BalanceArrived?.Invoke(this, balances);
        }

        void ProcessOrdersMassStatus(JObject messageObject)
        {
            var ordersMassStatus = messageObject.ToObject<OrdersMassStatusMessage>();
            if (ordersMassStatus.Account != tradingAccountId) return;
            if (ordersMassStatus.ActiveOrders == null)
            {
                ActiveOrdersListArrived?.Invoke(this, new List<OrderMessage>());
                return;
            }

            foreach (XenaOrderMessage activeOrderRaw in ordersMassStatus.ActiveOrders)
            {
                activeOrderRaw.SetBase();
                isinByOrderId.TryAdd(activeOrderRaw.OrderId, activeOrderRaw.Isin);
            }

            List<OrderMessage> activeOrders = ordersMassStatus.ActiveOrders.Select(order => (OrderMessage)order).ToList();
            ActiveOrdersListArrived?.Invoke(this, activeOrders);
        }

        void ProcessOrderCancelReject(JObject messageObject)
        {
            var reject = messageObject.ToObject<OrderCancelReject>();

            reject.SetReason();

            if (reject.OrderId != null) isinByOrderId.TryRemove(reject.OrderId, out _);
            ErrorOccured?.Invoke(this, new XenaErrorMessage((int)RequestError.CancelOrder, $"{reject.Reason}. {reject.RejectText}.", reject.OrderId));
        }

        void ProcessReject(JObject messageObject)
        {
            string text = (string)messageObject.SelectToken(Tags.Text);
            ErrorOccured?.Invoke(this, new XenaErrorMessage(0, "Previously sent request was rejected.", text));
        }

        void ProcessPositionReport(JObject messageObject)
        {
            int accountId = (int)messageObject.SelectToken(Tags.Account);
            if (accountId != tradingAccountId) return;

            var positions = messageObject.SelectToken(Tags.OpenPositions)?.ToObject<List<XenaPositionMessage>>();
            if (positions == null) return;

            foreach (XenaPositionMessage position in positions)
            {
                position.SetBase();
                if (!positionByIsin.TryGetValue(position.Isin, out XenaPositionMessage storedPosition) || position.Timestamp > storedPosition.Timestamp)
                    positionByIsin[position.Isin] = position;
            }

            PositionArrived?.Invoke(this, positions.Select(position => (PositionMessage)position).ToList());
        }

        void TradingWsThreadSafeSend(string message)
        {
            lock (tradingWsSendLocker)
            {
                tradingWs?.Send(message);
            }
        }

        //T SendRequest<T>(string method, string command, bool needSign, SortedList<string, string> parameters = null)
        //{
        //    if (parameters == null) parameters = new SortedList<string, string>();

        //    string jsonString = QueryString(method, command, needSign, parameters);
        //    if (string.IsNullOrEmpty(jsonString)) return default(T);
        //    JToken token = JToken.Parse(jsonString);
        //    T output;

        //    try
        //    {
        //        output = token.SelectToken("data").ToObject<T>();
        //    }
        //    catch (Exception e)
        //    {
        //        e.Data["response"] = jsonString;
        //        throw;
        //    }

        //    return output;
        //}

        //string QueryString(string method, string relativeUrl, bool needSign, SortedList<string, string> parameters)
        //{
        //    HttpWebRequest request = CreateHttpWebRequest(method, relativeUrl, needSign, parameters);
        //    return GetResponse(request, relativeUrl);
        //}

        //HttpWebRequest CreateHttpWebRequest(string method, string relativeUrl, bool needSign, SortedList<string, string> parameters)
        //{
        //    string paramsString = string.Join('&', parameters.Select(pair => $"{pair.Key}={pair.Value}"));

        //    HttpWebRequest request = WebRequest.CreateHttp($"{restBaseUri}{relativeUrl}{(string.IsNullOrEmpty(paramsString) ? "" : "?")}{paramsString}");
        //    request.Method = method;
        //    request.Timeout = Timeout.Infinite;

        //    return request;
        //}

        //string GetResponse(HttpWebRequest request, string relativeUrl)
        //{
        //    WebResponse response;
        //    try
        //    {
        //        response = request.GetResponse();
        //    }
        //    catch (WebException ex)
        //    {
        //        using (var exResponse = (HttpWebResponse)ex.Response)
        //        {
        //            ex.Data["Request"] = relativeUrl;
        //            Stream stream = exResponse?.GetResponseStream();
        //            if (stream == null) throw;

        //            using (var sr = new StreamReader(stream))
        //            {
        //                string responseString = sr.ReadToEnd();
        //                ex.Data["Response"] = responseString;

        //                ErrorOccured?.Invoke(this, new XenaErrorMessage(0, responseString, ex.Message));
        //                return null;
        //            }
        //        }
        //    }

        //    using (var sr = new StreamReader(response.GetResponseStream(), Encoding.ASCII))
        //    {
        //        string responseString = sr.ReadToEnd();
        //        return responseString;
        //    }
        //}
    }
}
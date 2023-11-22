using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using SharedDataStructures;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SharedTools;
using TmexConnector.Model;
using TmexConnector.Model.Public.Data;
using TmexConnector.Model.Public.Data.Req;
using TmexConnector.Model.Public.Data.Resp;
using TmexConnector.Model.Public.Messages;
using TmexConnector.Model.Shared;
using WebSocket4Net;
using ErrorEventArgs = SuperSocket.ClientEngine.ErrorEventArgs;

namespace TmexConnector
{
    public class TmexClient : ITradeConnector
    {
        const int DefaultPortfolioId = 1;
        readonly string wsBaseUrl = "wss://tmexbit.io/ws/v1";
        readonly JsonSerializerSettings serializerSettings = new JsonSerializerSettings {ContractResolver = new CamelCasePropertyNamesContractResolver()};
        readonly Dictionary<string, bool> gotSnapshots = new Dictionary<string, bool>();
        readonly Dictionary<string, BalanceMessage> currentBalances = new Dictionary<string, BalanceMessage>();
        readonly Dictionary<string, PositionMessage> currentPositions = new Dictionary<string, PositionMessage>();
        readonly object accountsLocker = new object();
        readonly ConcurrentDictionary<long, string> clientIdByHash = new ConcurrentDictionary<long, string>();
        readonly Dictionary<long, OrderMessage> ordersByExchangeId = new Dictionary<long, OrderMessage>();
        readonly object ordersLocker = new object();        
        readonly ConcurrentDictionary<long, string> clientIdByRequestId = new ConcurrentDictionary<long, string>();
        readonly ConcurrentDictionary<string, long> lastRevByIsin = new ConcurrentDictionary<string, long>();

        List<string> isins;
        string publicKey;
        string secretKey;

        Timer timeoutTimer;
        WebSocket ws;

        bool isConnected;
        bool gotAllSAnapshots;
        string storedOrderMessageString;

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
        public string ExchangeName => "tmex";
        public string PublicKey { get; private set; }

        //StreamWriter sw;
        //readonly object dumpLocker = new object();

        public void Init(List<string> isinsP, int dataTimeoutSeconds, string publicKeyP, string secretKeyP, string connectorName)
        {
            isins = isinsP;
            publicKey = publicKeyP;
            secretKey = secretKeyP;
            PublicKey = publicKeyP;

            Name = connectorName;
            //if (Name == "data_trade") sw = new StreamWriter("raw"){AutoFlush = true};

            if (dataTimeoutSeconds <= 0) return;
            timeoutTimer = new Timer(dataTimeoutSeconds * 1000);
            timeoutTimer.Elapsed += TimeoutTimer_Elapsed;
        }

        public void Start()
        {
            ws = new WebSocket(wsBaseUrl) {EnableAutoSendPing = true, AutoSendPingInterval = 1000, Security = { AllowNameMismatchCertificate = true}};
            ws.Opened += Ws_Opened;
            ws.Closed += Ws_Closed;
            ws.Error += Ws_Error;
            ws.MessageReceived += Ws_MessageReceived;
            ws.Open();
        }

        public void Stop()
        {
            DoStop();
        }

        public void AddOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            string orderMessageString = CreateOrderMessageAndStoreHash(clientOrderId, isin, side, price, qty, requestId);
            ws.Send(orderMessageString);
        }

        public void PrepareOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            storedOrderMessageString = CreateOrderMessageAndStoreHash(clientOrderId, isin, side, price, qty, requestId);
        }

        public Task SendPreparedOrder()
        {
            if (!string.IsNullOrEmpty(storedOrderMessageString))
            {
                ws.Send(storedOrderMessageString);
                storedOrderMessageString = null;
            }

            return Task.CompletedTask;
        }

        public void CancelOrder(string clientOrderId, int requestId)
        {
            var requestCancel = new ReqCancelOrder();
            if (clientOrderId.Contains("ex_"))
            {
                string[] tokens = clientOrderId.Split("_");
                long id = long.Parse(tokens[1]);
                requestCancel.OrderId = id;
            }
            else requestCancel.ExternalId = clientOrderId.GetStableHashCode();

            ApiMessage<ReqCancelOrder> cancelOrderMessage = ApiMessage.Make(ApiMessageType.CmdCancelOrder, requestId, requestCancel);
            string cancelOrderMessageString = JsonConvert.SerializeObject(cancelOrderMessage, serializerSettings);

            clientIdByRequestId.TryAdd(requestId, clientOrderId);

            ws.Send(cancelOrderMessageString);
        }

        public void ReplaceOrder(string oldClientOrderId, string newClientOrderId, decimal price, decimal qty, int requestId)
        {
            throw new NotImplementedException();
        }

        public void GetActiveOrders(int requestId)
        {
            List<OrderMessage> activeOrders;

            lock (ordersLocker) activeOrders = ordersByExchangeId.Values.ToList();

            ActiveOrdersListArrived?.Invoke(this, activeOrders);
        }

        public void GetPosAndMoney(int requestId)
        {
            List<BalanceMessage> balances;
            List<PositionMessage> positions;

            lock (accountsLocker)
            {
                balances = currentBalances.Values.ToList();
                positions = currentPositions.Values.ToList();
            }

            BalanceArrived?.Invoke(this, balances);
            PositionArrived?.Invoke(this, positions);
        }

        void TimeoutTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            DoStop();
            Disconnected?.Invoke(this, null);
        }

        void DoStop()
        {
            timeoutTimer?.Stop();
            if (ws?.State == WebSocketState.Open) ws.Close();

            isConnected = false;

            lock (accountsLocker)
            {
                currentBalances.Clear();
                currentPositions.Clear();
            }
        }

        void Ws_Opened(object sender, EventArgs e)
        {
            
        }

        void Ws_Closed(object sender, EventArgs e)
        {
            DoStop();
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
            var messageType = messageObject.SelectToken("t").ToObject<ApiMessageType>();

            switch (messageType)
            {
                case ApiMessageType.Book:
                    timeoutTimer?.Stop();
                    ProcessBook(messageObject); //lock(dumpLocker) sw.WriteLine(DateTime.UtcNow.ToString("HH:mm:ss.ffff;") + message);
                    timeoutTimer?.Start();
                    //else  lock(dumpLocker) sw.WriteLine(DateTime.UtcNow.ToString("HH:mm:ss.ffff;") + message + ";DUPLICATE");
                    break;

                case ApiMessageType.Quote:
                    timeoutTimer?.Stop();
                    ProcessQuote(messageObject);
                    timeoutTimer?.Start();
                    break;

                case ApiMessageType.Order:
                    ProcessOrders(messageObject);
                    break;

                case ApiMessageType.Account:
                    ProcessAccounts(messageObject);
                    break;

                case ApiMessageType.System:
                    if (!isConnected) ProcessConnection(messageObject);
                    isConnected = true;
                    break;

                case ApiMessageType.CmdAuthenticate:
                    ProcessAuthentication(messageObject);
                    break;

                case ApiMessageType.CmdSubscribe:
                    CheckSubscriptionErrors(messageObject);
                    break;

                case ApiMessageType.CmdPlaceOrder:
                    ProcessAddOrderResponse(messageObject);
                    break;

                case ApiMessageType.CmdCancelOrder:
                    ProcessCancelOrderResponse(messageObject);
                    break;

                case ApiMessageType.Error:
                    ProcessError(messageObject);
                    break;
            }
        }

        void Authenticate()
        {
            long nonce = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            string payload = $"AUTH{nonce}";

            byte[] secretHash = Convert.FromBase64String(secretKey);
            string signature;
            using (var hmac = new HMACSHA512(secretHash))
            {
                signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
            }

            ApiMessage<ReqApiKeyLogin> authenticateMessage = ApiMessage.Make(ApiMessageType.CmdAuthenticate,
                                                                             0,
                                                                             new ReqApiKeyLogin
                                                                             {
                                                                                 Key = publicKey,
                                                                                 Payload = payload,
                                                                                 Nonce = nonce.ToString(),
                                                                                 Signature = signature,
                                                                                 DefaultPortfolioId = DefaultPortfolioId
                                                                             });

            string authenticateMessageString = JsonConvert.SerializeObject(authenticateMessage, serializerSettings);

            ws.Send(authenticateMessageString);
        }

        void Subscribe()
        {
            var streams = new List<string>{"accounts", "orders"};

            if (isins != null)
            {
                foreach (string isin in isins)
                {
                    streams.Add($"book.{isin}");
                    streams.Add($"quote.{isin}");

                    gotSnapshots[isin] = false;
                    lastRevByIsin[isin] = -1;
                }

                gotAllSAnapshots = false;
            }

            ApiMessage<string> subscribeMessage = ApiMessage.Make(ApiMessageType.CmdSubscribe, 0, streams.ToArray());
            string subscribeMessageString = JsonConvert.SerializeObject(subscribeMessage, serializerSettings);

            ws.Send(subscribeMessageString);
        }

        string CreateOrderMessageAndStoreHash(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            int idHash = clientOrderId.GetStableHashCode();
            ApiMessage<ReqPlaceOrder> orderMessage = ApiMessage.Make(ApiMessageType.CmdPlaceOrder,
                                                                     requestId,
                                                                     new ReqPlaceOrder
                                                                     {
                                                                         PortfolioId = DefaultPortfolioId,
                                                                         Amount = (long)qty,
                                                                         Symbol = isin,
                                                                         Price = price,
                                                                         Side = side == OrderSide.Buy ? TradeSide.Buy : TradeSide.Sell,
                                                                         ExternalId = idHash,
                                                                         Flag = OrderFlags.PostOnly
                                                                     });
            string orderMessageString = JsonConvert.SerializeObject(orderMessage, serializerSettings);

            clientIdByHash[idHash] = clientOrderId;

            return orderMessageString;
        }

        void ProcessConnection(JObject messageObject)
        {
            var systemEventMessage = messageObject.ToObject<ApiMessage<SystemEvent>>();
            if (IsNull(systemEventMessage) || IsEmpty(systemEventMessage) || IsMultiple(systemEventMessage))
                throw new ExchangeApiException($"Connection failed. No data in message for {Name} connector.");

            SystemEvent systemEvent = systemEventMessage.Data[0];

            if (systemEvent.Status != SystemStatus.Online)
                throw new ExchangeApiException($"Connection failed with status={systemEvent.Status} and message={systemEvent.Message} for {Name} connector.");           

            Authenticate();
        }

        void ProcessAuthentication(JObject messageObject)
        {
            var authenticationReportMessage = messageObject.ToObject<ApiMessage<ResAuthentication>>();
            if (IsNull(authenticationReportMessage) || IsEmpty(authenticationReportMessage) || IsMultiple(authenticationReportMessage)) return;

            ResAuthentication authenticationReport = authenticationReportMessage.Data[0];
            
            if (!authenticationReport.IsAuthenticated) throw new ExchangeApiException($"Authentication failed without message for {Name} connector.");

            Connected?.Invoke(this, null);

            Subscribe();
        }

        void CheckSubscriptionErrors(JObject messageObject)
        {
            var subscribeReportMessage = messageObject.ToObject<ApiMessage<StreamSubscription>>();
            if (IsNull(subscribeReportMessage) || IsEmpty(subscribeReportMessage)) return;

            List<StreamSubscription> errorStreams = subscribeReportMessage.Data.Where(report => report.Error != null).ToList();

            if (errorStreams.Count <= 0)
            {
                if (isins != null && isins.Count > 0) timeoutTimer?.Start();
                return;
            }

            var ex = new ExchangeApiException($"Could not subscribe to streams for connector {Name}.");
            foreach (StreamSubscription errorStream in errorStreams)
                ex.Data[errorStream.Stream] = $"ErrorCode={errorStream.Error.Code};ErrorMessage={errorStream.Error.Message}.";

            throw ex;
        }

        void ProcessBook(JObject messageObject)
        {
            var bookMessage = messageObject.ToObject<WsStreamMessage<OrderBookLevel, OrderBookStream>>();
            if (IsNull(bookMessage)) return;

            if (bookMessage.Stream.Revision > lastRevByIsin[bookMessage.Stream.Symbol]) lastRevByIsin[bookMessage.Stream.Symbol] = bookMessage.Stream.Revision;
            else return;

            var book = new TmexBookMessage(bookMessage.Stream.Symbol, bookMessage.Stream.Revision, bookMessage.Data);
            if (gotAllSAnapshots || gotSnapshots[book.Isin]) BookUpdateArrived?.Invoke(this, book);
            else
            {
                BookSnapshotArrived?.Invoke(this, book);
                gotSnapshots[book.Isin] = true;
                if (gotSnapshots.Values.All(got => got)) gotAllSAnapshots = true;
            }
        }

        void ProcessQuote(JObject messageObject)
        {
            var quoteMessage = messageObject.ToObject<WsStreamMessage<Quote, AssetStream>>();
            if (IsNull(quoteMessage) || IsEmpty(quoteMessage)) return;

            Quote ticker = quoteMessage.Data.Last();
            ticker.SetIsin(quoteMessage.Stream.Symbol);

            TickerArrived?.Invoke(this, ticker);
        }

        void ProcessAccounts(JObject messageObject)
        {
            var accounts = messageObject.ToObject<WsStreamMessage<AccountData, BasicStream>>();
            if (IsNull(accounts) || IsEmpty(accounts)) return;

            lock (accountsLocker)
            {
                foreach (AccountData accountData in accounts.Data)
                {
                    if (accountData.PortfolioId != DefaultPortfolioId) continue;

                    if (accountData.IsPayment)
                    {
                        currentBalances[accountData.Symbol] =
                            new TmexBalanceMessage(accountData.Symbol, accountData.CurrentVolume - accountData.LockedMargin, accountData.LockedMargin);
                    }
                    else currentPositions[accountData.Symbol] = new TmexPositionMessage(accountData.Symbol, accountData.CurrentVolume);
                }
            }
        }

        void ProcessOrders(JObject messageObject)
        {
            var orders = messageObject.ToObject<WsStreamMessage<ClientOrder, BasicStream>>();
            if (IsNull(orders)) return;

            foreach (ClientOrder order in orders.Data)
            {
                //либо не мы выставляли, либо руками, либо выставляли до перезапуска, поэтому не найдена
                if (order.ClientHashId == 0 || !clientIdByHash.TryGetValue((int)order.ClientHashId, out string clientOrderId))
                    order.SetClientOrderId("ex_" + order.ExchangeOrderId);
                else order.SetClientOrderId(clientOrderId); //мы выставляли

                switch (order.State)
                {
                    case OrderState.Active:
                        bool couldAdd;
                        lock (ordersLocker) couldAdd = ordersByExchangeId.TryAdd(order.ExchangeOrderId, order);
                        if (couldAdd) NewOrderAdded?.Invoke(this, order);

                        if (order.TradeQty > 0) ExecutionReportArrived?.Invoke(this, order);
                        break;

                    case OrderState.Cancelled:
                        lock (ordersLocker) ordersByExchangeId.Remove(order.ExchangeOrderId);
                        if (order.ClientHashId != 0) clientIdByHash.Remove(order.ClientHashId, out _);

                        OrderCanceled?.Invoke(this, order);
                        break;

                    case OrderState.Filled:
                        lock (ordersLocker) ordersByExchangeId.Remove(order.ExchangeOrderId);
                        if (order.ClientHashId != 0) clientIdByHash.Remove(order.ClientHashId, out _);

                        if (order.TradeQty > 0) ExecutionReportArrived?.Invoke(this, order);
                        break;
                }               
            }
        }

        void ProcessAddOrderResponse(JObject messageObject)
        {
            var addOrderResponseMessage = messageObject.ToObject<ApiMessage<ResPlacedOrder>>();
            if (IsNull(addOrderResponseMessage) || IsEmpty(addOrderResponseMessage) || IsMultiple(addOrderResponseMessage)) return;

            ResPlacedOrder addOrderResponse = addOrderResponseMessage.Data[0];

            if (addOrderResponse.Error == null) return;

            string externalId = addOrderResponse.ExternalId.ToString();
            if (!clientIdByHash.Remove(addOrderResponse.ExternalId, out string clientOrderId))
                ErrorOccured?.Invoke(this, new TmexErrorMessage(0, "Could not find clientOrderId hash in dictionary.", externalId));

            string orderId = clientOrderId ?? externalId;
            ApiError error = addOrderResponse.Error;
            ErrorOccured?.Invoke(this, new TmexErrorMessage((int)RequestError.AddOrder, $"Adding order failed. Message={error.Message}. Code={error.Code}", orderId));            
        }

        void ProcessCancelOrderResponse(JObject messageObject)
        {
            var cancelOrderResponseMessage = messageObject.ToObject<ApiMessage<ApiError>>();
            string clientOrderId = null;
            if (cancelOrderResponseMessage.Reference > 0)
            {
                if (!clientIdByRequestId.Remove(cancelOrderResponseMessage.Reference, out clientOrderId))
                    ErrorOccured?.Invoke(this,
                                         new TmexErrorMessage(0, "Could not find requestId in dictionary.", cancelOrderResponseMessage.Reference.ToString()));

            }

            if (cancelOrderResponseMessage.Data == null || cancelOrderResponseMessage.Data.Length == 0 || cancelOrderResponseMessage.Data[0] == null) return;
            if (IsMultiple(cancelOrderResponseMessage)) return;

            string orderId = clientOrderId ?? "NA";
            ApiError error = cancelOrderResponseMessage.Data[0];
            ErrorOccured?.Invoke(this, new TmexErrorMessage((int)RequestError.CancelOrder, $"Cancel order failed. Message={error.Message}. Code={error.Code}", orderId));   
        }

        void ProcessError(JObject messageObject)
        {
            var errorMessage = messageObject.ToObject<ApiMessage>();
            if (errorMessage.Error == null) return;

            ApiError error = errorMessage.Error;
            ErrorOccured?.Invoke(this, new TmexErrorMessage(0, error.Code.ToString(), error.Message));            
        }

        bool IsNull<T>(ApiMessage<T> message)
        {
            if (message.Data == null)
            {
                ErrorOccured?.Invoke(this, new TmexErrorMessage(0, $"No data in message {message.Type}", ""));
                return true;
            }

            return false;
        }

        bool IsEmpty<T>(ApiMessage<T> message)
        {
            if (message.Data != null && message.Data.Length == 0)
            {
                ErrorOccured?.Invoke(this, new TmexErrorMessage(0, $"No data in message {message.Type}", ""));
                return true;
            }

            return false;
        }

        bool IsMultiple<T>(ApiMessage<T> message)
        {
            if (message.Data != null && message.Data.Length > 1)
            {
                ErrorOccured?.Invoke(this, new TmexErrorMessage(0, $"{message.Data.Length} data objects in message {message.Type}", ""));
                return true;
            }

            return false;
        }
    }
}

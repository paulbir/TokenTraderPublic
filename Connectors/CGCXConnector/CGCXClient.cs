using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using CGCXConnector.Model;
using Newtonsoft.Json;
using SharedDataStructures;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SharedTools;
using SuperSocket.ClientEngine;
using WebSocket4Net;

namespace CGCXConnector
{
    public class CGCXClient : ITradeConnector
    {
        static readonly string baseUri = "wss://api.exchange.cgcx.io/WSGateway/";
        List<string> isins;
        string publicKey;
        string secretKey;
        //int userId;
        Timer timeoutTimer;
        WebSocket ws;
        int internalRequestId = 2;
        int userAccountId;
        string preparedOrder = "";

        readonly ConcurrentMap<int, string> isinById = new ConcurrentMap<int, string>();
        readonly ConcurrentDictionary<int, string> clientOrderIdByRequest = new ConcurrentDictionary<int, string>();
        readonly ConcurrentMap<long, string> clientIdByOrderId = new ConcurrentMap<long, string>();
        readonly HashSet<long> gotNewOrderMessageIds = new HashSet<long>();

        Dictionary<string, int> currencyIdsByName;

        public string Name { get; private set; }
        public string ExchangeName => "cgcx";
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
            publicKey = publicKeyP;
            secretKey = secretKeyP;
            PublicKey = publicKeyP;
            //if (!int.TryParse(accountIdP, out userId)) throw new ConfigErrorsException($"AccountId {accountIdP} has to be integer for CGCX connector.");

            Name = connectorName;

            if (dataTimeoutSeconds <= 0) return;
            timeoutTimer = new Timer(dataTimeoutSeconds * 1000);
            timeoutTimer.Elapsed += TimeoutTimer_Elapsed;
        }

        public void Start()
        {
            isinById.Clear();
            ws = new WebSocket(baseUri) {EnableAutoSendPing = true, AutoSendPingInterval = 1000};

            ws.Opened += Ws_Opened;
            ws.Closed += Ws_Closed;
            ws.Error += Ws_Error;
            ws.MessageReceived += Ws_MessageReceived;
            ws.Security.AllowNameMismatchCertificate = true;

            ws.Open();
        }

        public void Stop()
        {
            timeoutTimer?.Stop();
            ws?.Close();
        }

        public void AddOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            requestId += internalRequestId;
            int isinId = isinById.Reverse[isin];
            int intClientOrderId = clientOrderId.GetStableHashCode();
            int intSide = side == OrderSide.Buy ? 0 : 1;
            string priceStr = price.ToString(CultureInfo.InvariantCulture);
            string qtyStr = qty.ToString(CultureInfo.InvariantCulture);

            if (!clientOrderIdByRequest.TryAdd(requestId, clientOrderId))
            {
                var error = new CGCXErrorMessage((int)RequestError.AddOrder,
                                                 $"Dictionary already contains requestId={requestId} or clientOrderId={clientOrderIdByRequest[requestId]}",
                                                 "");
                ErrorOccured?.Invoke(this, error);
                return;
            }

            string addOrderMessage = MessageCreator.CreateAddOrderMessage(userAccountId, intClientOrderId, isinId, intSide, priceStr, qtyStr, requestId);
            ws.Send(addOrderMessage);
        }

        public void PrepareOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            requestId += internalRequestId;
            int isinId = isinById.Reverse[isin];
            int intClientOrderId = clientOrderId.GetStableHashCode();
            int intSide = side == OrderSide.Buy ? 0 : 1;
            string priceStr = price.ToString(CultureInfo.InvariantCulture);
            string qtyStr = qty.ToString(CultureInfo.InvariantCulture);

            if (!clientOrderIdByRequest.TryAdd(requestId, clientOrderId))
            {
                var error = new CGCXErrorMessage((int)RequestError.AddOrder,
                                                 $"Dictionary already contains requestId={requestId} or clientOrderId={clientOrderIdByRequest[requestId]}",
                                                 "");
                ErrorOccured?.Invoke(this, error);
                return;
            }

            preparedOrder = MessageCreator.CreateAddOrderMessage(userAccountId, intClientOrderId, isinId, intSide, priceStr, qtyStr, requestId);
        }

        public Task SendPreparedOrder()
        {
            ws.Send(preparedOrder);
            preparedOrder = "";
            return Task.CompletedTask;
        }

        public void CancelOrder(string clientOrderId, int requestId)
        {
            requestId += internalRequestId;
            if (!clientIdByOrderId.Reverse.TryGetValue(clientOrderId, out long orderId))
            {
                var error = new CGCXErrorMessage(-1, $"Map doesn't contain clientOrderId={clientOrderId}", "");
                ErrorOccured?.Invoke(this, error);
                return;
            }

            int intClientOrderId = clientOrderId.GetStableHashCode();
            string cancelOrderMessage = MessageCreator.CreateCancelOrderMessage(userAccountId, intClientOrderId, orderId, requestId);
            ws.Send(cancelOrderMessage);
        }

        public void ReplaceOrder(string oldClientOrderId, string newClientOrderId, decimal price, decimal qty, int requestId)
        {
            throw new NotImplementedException();
        }

        public void GetActiveOrders(int requestId)
        {
            requestId += internalRequestId;
            string getActiveOrdersMessage = MessageCreator.CreateGetActiveOrdersMessage(userAccountId, requestId);
            ws.Send(getActiveOrdersMessage);
        }

        public void GetPosAndMoney(int requestId)
        {
            requestId += internalRequestId;
            string getBalanceMessage = MessageCreator.CreateGetTradingBalanceMessage(userAccountId, requestId);
            ws.Send(getBalanceMessage);
        }

        void TimeoutTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            timeoutTimer?.Stop();
            Disconnected?.Invoke(this, null);
        }

        void Ws_Opened(object sender, EventArgs e)
        {
            //long nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            //string signature = MakeSignature(nonce);
            string loginMessage = MessageCreator.CreateWebLoginMessage(publicKey, secretKey, internalRequestId++);

            //string loginMessage = MessageCreator.CreateLoginMessage(publicKey, userId, nonce, signature, internalRequestId++);
            ws.Send(loginMessage);

            timeoutTimer?.Start();
            Connected?.Invoke(this, null);
        }

        void Ws_Closed(object sender, EventArgs e)
        {
            timeoutTimer?.Stop();
            Disconnected?.Invoke(this, null);
        }

        void Ws_Error(object sender, ErrorEventArgs e)
        {
            throw e.Exception;
        }

        void Ws_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            string message = e.Message;

            var rawBase = JsonConvert.DeserializeObject<RawBaseMessage>(message);

            timeoutTimer?.Stop();

            switch (rawBase.RequestType)
            {
                case "Level2UpdateEvent":
                    ProcessBook(rawBase.Payload, BookUpdateArrived);
                    break;

                case "OrderStateEvent":
                    ProcessOrderEvent(rawBase.Payload);
                    break;

                case "SendOrder":
                    ProcessSendOrderResponse(rawBase.Payload, rawBase.RequestId);
                    break;

                case "GetAccountPositions":
                    ProcessPositions(rawBase.Payload);
                    break;

                case "GetOpenOrders":
                    ProcessOpenOrders(rawBase.Payload);
                    break;

                //case "TickerDataUpdateEvent":
                //    ProcessTicker(rawBase.Payload);
                //    break;

                case "WebAuthenticateUser":
                    ProcessLogin(rawBase.Payload);
                    break;

                case "GetUserInfo":
                    ProcessUserInfo(rawBase.Payload);
                    break;

                case "GetInstruments":
                    ProcessInstruments(rawBase.Payload);
                    break;

                case "SubscribeAccountEvents":
                    ProcessAccountEventsSubscribtion(rawBase.Payload);
                    break;

                case "SubscribeLevel2":
                    ProcessBook(rawBase.Payload, BookSnapshotArrived);
                    break;

                case "GetProducts":
                    ProcessProducts(rawBase.Payload);
                    break;

                case "CreateWithdrawTicket":
                    ProcessWithdraw(rawBase.Payload);
                    break;
            }

            timeoutTimer?.Start();
        }

        void ProcessLogin(string message)
        {
            var logonMessage = JsonConvert.DeserializeObject<CGCXLogonMessage>(message);
            if (!logonMessage.IsLogonSuccessfull)
                throw new ConfigErrorsException($"Logon failed for {Name} connector publicKey={publicKey}.");


            //timeoutTimer?.Start();
            //Connected?.Invoke(this, null);

            string getUserInfoMessage = MessageCreator.CreateGetUserInfoMessage(internalRequestId++);
            ws.Send(getUserInfoMessage);
        }

        void ProcessUserInfo(string message)
        {
            var userInfoMessage = JsonConvert.DeserializeObject<User>(message);

            userAccountId = userInfoMessage.AccountId;

            string subscribeToExecutionReportsMessage = MessageCreator.CreateSubscribeToReportsMessage(userAccountId, internalRequestId++);
            ws.Send(subscribeToExecutionReportsMessage);

            string getInstrumentsMessage = MessageCreator.CreateGetInstrumentsMessage(internalRequestId++);
            ws.Send(getInstrumentsMessage);

            //string getProductsMessage = MessageCreator.CreateGetProductsMessage(internalRequestId++);
            //ws.Send(getProductsMessage);
        }

        void ProcessInstruments(string message)
        {
            var instruments = JsonConvert.DeserializeObject<List<Instrument>>(message);

            foreach (Instrument instrument in instruments) isinById.TryAdd(instrument.Id, instrument.Isin);

            //CancelOrder("neworder1", internalRequestId++);
            //AddOrder("neworder1", "ETHBTC", OrderSide.Sell, 0.0701m, 0.0001m, internalRequestId++);
            //GetBalance(internalRequestId++);
            //GetActiveOrders(internalRequestId++);
            //return;

            if (isins == null) return;

            foreach (string isin in isins)
            {
                if (!isinById.Reverse.TryGetValue(isin, out int isinId))
                    throw new ConfigErrorsException($"Isin {isin} was not found in CGCX instruments.");

                string subscribeBookMessage = MessageCreator.CreateSubscribeToBookMessage(isin, internalRequestId++);
                ws.Send(subscribeBookMessage);

                //string subscribeTickerMessage = MessageCreator.CreateSubscribeToTickerMessage(isinId, internalRequestId++);
                //ws.Send(subscribeTickerMessage);
            }
        }

        void ProcessAccountEventsSubscribtion(string message)
        {
            var subscribeMessage = JsonConvert.DeserializeObject<Subscribe>(message);
            if (!subscribeMessage.IsSubscribed)
                throw new ConfigErrorsException($"Subscribtion to account events failed for {Name} connector publicKey={publicKey};accountId={userAccountId}.");
        }

        void ProcessBook(string message, EventHandler<BookMessage> bookEvent)
        {
            var rawBook = JsonConvert.DeserializeObject<List<List<decimal>>>(message);
            var book = new CGCXBookMessage(rawBook);

            //int numIds = new HashSet<decimal>(rawBook.Select(level => level[7])).Count;
            if (!isinById.Forward.TryGetValue(book.IsinId, out string isin)) return;

            book.SetIsin(isin);
            bookEvent?.Invoke(this, book);

            var ticker = new CGCXTickerMessage(isin, book.BestBid, book.BestAsk, book.Last);
            TickerArrived?.Invoke(this, ticker);
        }

        void ProcessTicker(string message)
        {
            var rawTickers = JsonConvert.DeserializeObject<List<List<decimal>>>(message);            
            var lastTickersByIsinId = new Dictionary<int, CGCXTickerMessage>();

            foreach (List<decimal> rawTicker in rawTickers)
            {
                var ticker = new CGCXTickerMessage(rawTicker);

                if (!isinById.Forward.TryGetValue(ticker.IsinId, out string isin)) continue;

                ticker.SetIsin(isin);
                if (!lastTickersByIsinId.TryGetValue(ticker.IsinId, out CGCXTickerMessage lastTicker) || ticker.EndTimestamp > lastTicker.EndTimestamp)
                    lastTickersByIsinId[ticker.IsinId] = ticker;
            }

            foreach (CGCXTickerMessage ticker in lastTickersByIsinId.Values) TickerArrived?.Invoke(this, ticker);
        }

        void ProcessSendOrderResponse(string message, int requestId)
        {
            var sendOrderResponse = JsonConvert.DeserializeObject<SendOrderResponse>(message);

            //допускаем, что может быть заявка с requestId, которую я не выставлял. мало ли. может быть руками выставили.
            if (!clientOrderIdByRequest.TryRemove(requestId, out string clientOrderId)) return;

            if (sendOrderResponse.Status != "Accepted")
            {
                var error = new CGCXErrorMessage((int)RequestError.AddOrder,
                                                 sendOrderResponse.Error,
                                                 $"Adding order failed for requestId={requestId} and clientOrderId={clientOrderId}.");
                ErrorOccured?.Invoke(this, error);
                return;
            }

            if (!clientIdByOrderId.TryAdd(sendOrderResponse.OrderId, clientOrderId))
            {
                var error = new CGCXErrorMessage((int)RequestError.AddOrder,
                                                 $"Map already contains orderId={sendOrderResponse.OrderId} or clientOrderId={clientOrderId}",
                                                 "");
                ErrorOccured?.Invoke(this, error);
            }
        }

        void ProcessOrderEvent(string message)
        {
            var order = JsonConvert.DeserializeObject<CGCXOrderMessage>(message);
            string isin = isinById.Forward[order.IsinId];

            //допускаем, что может быть заявка с orderId, которую я не выставлял. мало ли. может быть руками выставили или сняли.
            if (!clientIdByOrderId.Forward.TryGetValue(order.ExchangeOrderId, out string clientOrderId)) return;

            order.SetIsinAndId(isin, clientOrderId);

            switch (order.ChangeReason)
            {
                case "NewInputAccepted":
                    NewOrderAdded?.Invoke(this, order);
                    gotNewOrderMessageIds.Add(order.ExchangeOrderId);
                    break;

                case "Trade":
                    if (order.Status == "FullyExecuted")
                    {
                        //здесь не приходит сообщение о новой заявке, если она сразу свелась. сразу приходит execution report.
                        //поэтому эмулируем новую заявку, если не получили сообщение о новой ранее.
                        if (!gotNewOrderMessageIds.Contains(order.ExchangeOrderId))
                        {
                            CGCXOrderMessage newOrder = order.CreateNewFromExecuted();
                            NewOrderAdded?.Invoke(this, newOrder);
                        }

                        clientIdByOrderId.Remove(order.ExchangeOrderId, clientOrderId);
                        gotNewOrderMessageIds.Remove(order.ExchangeOrderId);
                    }
                    ExecutionReportArrived?.Invoke(this, order);
                    break;

                case "UserModified":
                    if (order.Status == "Canceled")
                    {
                        OrderCanceled?.Invoke(this, order);
                        clientIdByOrderId.Remove(order.ExchangeOrderId, clientOrderId);
                    }
                    else OrderReplaced?.Invoke(this, order);

                    break;

                case "NewInputRejected":
                case "OtherRejected":
                case "Expired":
                case "SystemCanceled_NoMoreMarket":
                case "SystemCanceled_BelowMinimum":
                case "NoChange":
                    var error = new CGCXErrorMessage((int)RequestError.AddOrder,
                                                     $"Order rejected or canceled by system or unexpected reason: " +
                                                     $"ChangeReason={order.ChangeReason} for order {order}.",
                                                     "");
                    ErrorOccured?.Invoke(this, error);
                    break;

                default: throw new ExchangeApiException($"Unknown ChangeReason={order.ChangeReason} for order {order}.");
            }
        }

        void ProcessPositions(string message)
        {
            List<BalanceMessage> balances;
            try
            {
                balances = JsonConvert.DeserializeObject<List<CGCXBalanceMessage>>(message).
                                        Select(balance => (BalanceMessage)balance).
                                        ToList();
            }
            catch (Exception ex)
            {
                ex.Data["Response"] = message;
                //return;
                throw;
            }

            BalanceArrived?.Invoke(this, balances);
        }

        void ProcessOpenOrders(string message)
        {
            var rawOrders = JsonConvert.DeserializeObject<List<CGCXOrderMessage>>(message);
            var orders = new List<OrderMessage>();

            foreach (CGCXOrderMessage rawOrder in rawOrders)
            {
                string isin = isinById.Forward[rawOrder.IsinId];

                //может быть заявка, которую либо не я выставлял, либо после выставления торговалка упала.
                // генерируем сами для неё clientOrderId=ExchangeOrderId, чтобы потом можно было её снять.
                if (!clientIdByOrderId.Forward.TryGetValue(rawOrder.ExchangeOrderId, out string clientOrderId))
                {
                    clientOrderId = rawOrder.ExchangeOrderId.ToString();
                    clientIdByOrderId.TryAdd(rawOrder.ExchangeOrderId, clientOrderId);
                }

                rawOrder.SetIsinAndId(isin, clientOrderId);
                orders.Add(rawOrder);
            }

            ActiveOrdersListArrived?.Invoke(this, orders);
        }

        void ProcessProducts(string message)
        {
            var currencies = JsonConvert.DeserializeObject<List<CGCXProductMessage>>(message);
            currencyIdsByName = currencies.ToDictionary(keySelector: product => product.Currency, elementSelector: product => product.CurrencyId);

            //Withdraw();
        }

        void ProcessWithdraw(string message)
        {
            Console.WriteLine(message);
        }

        string MakeSignature(long nonce)
        {
            string message = $"{nonce}{userAccountId}{publicKey}";
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            byte[] secretKeyBytes = Encoding.UTF8.GetBytes(secretKey);

            byte[] hashBytes;
            using (var encoder = new HMACSHA256(secretKeyBytes))
            {
                hashBytes = encoder.ComputeHash(messageBytes);
            }

            string hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            return hashString;
        }

        void Withdraw()
        {
            string withdrawMessage =
                MessageCreator.CreateWithdrawTicketMessage(userAccountId,
                                                           currencyIdsByName["BTC"],
                                                           0.34m,
                                                           "1MTaWALYSFcyUDPDFLeWE4jQsa1gvQMcpz",
                                                           internalRequestId++);
            
            ws.Send(withdrawMessage);
        }
    }
}
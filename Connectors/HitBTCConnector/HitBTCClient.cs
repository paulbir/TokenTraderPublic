using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using HitBTCConnector.Model;
using Newtonsoft.Json;
using SharedDataStructures;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SuperSocket.ClientEngine;
using WebSocket4Net;

namespace HitBTCConnector
{
    public class HitBTCClient : ITradeConnector
    {
        enum ErrorSeverity
        {
            Fatal,
            Temporary,
            Recoverable,
            Unknown
        }

        enum Request
        {
            Login,
            ReportSubscribtion,
            BookSubscribtion,
            TickerSubscribtion,
            AddOrder,
            CancelOrder,
            ReplaceOrder,
            ActiveOrders,
            TradingBalance
        }

        static readonly string baseUri = "wss://api.hitbtc.com/api/2/ws";
        readonly ConcurrentQueue<Request> lastRequests = new ConcurrentQueue<Request>();
        List<string> isins;

        //дефолтные значения для получения данных
        string publicKey = "";
        string secretKey = "";
        System.Timers.Timer timeoutTimer;
        WebSocket ws;
        string preparedOrder;

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

        void TimeoutTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            timeoutTimer?.Stop();
            Disconnected?.Invoke(this, null);
        }

        void SendRequest(string request, Request newWaitingState, bool asyncRequest = false)
        {
            lastRequests.Enqueue(newWaitingState);
            int errorCode = -1;
            switch (newWaitingState)
            {
                    case Request.ActiveOrders:
                        errorCode = (int)RequestError.ActiveOrders;
                        break;

                    case Request.TradingBalance:
                        errorCode = (int)RequestError.TradingBalance;
                        break;

                    case Request.AddOrder:
                        errorCode = (int)RequestError.AddOrder;
                        break;
            }

            if (ws?.State == WebSocketState.Open)
            {
                if (asyncRequest) Task.Run(() => ws.Send(request));
                else ws.Send(request);
            }
            else ErrorOccured?.Invoke(this, 
                                      new HitBTCErrorMessage(errorCode, 
                                                             $"Can't send request {request}. Socket is not open yet.", 
                                                             "Wait for socket to open."));
        }

        static (string, string) ExtractResponse(string message)
        {
            int firstCommaIndex = message.IndexOf(",", StringComparison.InvariantCulture);
            int firstColonIndex = message.IndexOf("\"", firstCommaIndex, StringComparison.InvariantCulture);
            int secondColonIndex = message.IndexOf("\"", firstColonIndex + 1, StringComparison.InvariantCulture);
            string responseType = message.Substring(firstColonIndex + 1, secondColonIndex - firstColonIndex - 1);
            string responseTrueStringContainer = message.Substring(secondColonIndex, 10);

            //в этом случае нету никакого payload с фигурными скобками. у result есть только значение true.
            if (responseType == "result" && responseTrueStringContainer.Contains("true")) return (responseType, "true");

            string responsePayload;
            int openBraceIndex = message.IndexOf("{", secondColonIndex + 1, StringComparison.InvariantCulture);
            int openBracketIndex = openBraceIndex > 0 ? openBraceIndex - 1 : message.IndexOf("[", secondColonIndex + 1, StringComparison.InvariantCulture);
            if (openBracketIndex > 0 && message[openBracketIndex] == '[') //массив
            {
                int closeBracketIndex = message.LastIndexOf("]", message.Length - 1, StringComparison.InvariantCulture);
                responsePayload = message.Substring(openBracketIndex, closeBracketIndex - openBracketIndex + 1);
            }
            else //объект
            {
                int lastBraceIndex = message.LastIndexOf("}", message.Length - 1, StringComparison.InvariantCulture);
                int closeBraceIndex = message.LastIndexOf("}", lastBraceIndex - 1, StringComparison.InvariantCulture);
                responsePayload = message.Substring(openBraceIndex, closeBraceIndex - openBraceIndex + 1);
            }

            return (responseType, responsePayload);
        }

        static string GetResponseMethod(string message)
        {
            int methodIndex = message.IndexOf("method\":", StringComparison.InvariantCulture);
            int commaIndex = message.IndexOf(",", methodIndex, StringComparison.InvariantCulture);
            return message.Substring(methodIndex + 9, commaIndex - methodIndex - 10);
        }

        static ErrorSeverity GetErrorSeverity(int errorCode)
        {
            switch (errorCode)
            {
                case 403: //Action is forbidden for account
                case 1001: //Authorisation required
                case 1002: //Authorisation failed
                case 1003: //Action is forbidden for this API key
                case 1004: //Unsupported authorisation method
                case 2001: //Symbol not found 	
                case 2002: //Currency not found 
                case 10001: //Validation error
                    return ErrorSeverity.Fatal;

                case 500: //Internal Server Error 	
                case 503: //Service Unavailable
                case 504: //Gateway Timeout
                    return ErrorSeverity.Temporary;

                case 429: //Too many requestsm 
                case 20001: //Insufficient funds
                case 20002: //Order not found
                case 20004: //Transaction not found
                case 20008: //Duplicate clientOrderId
                    return ErrorSeverity.Recoverable;

                default: return ErrorSeverity.Unknown;
            }
        }

        #region Interface implementation
        public string Name { get; private set; }
        public string ExchangeName => "hitbtc";
        public string PublicKey { get; private set; }

        public void Init(List<string> isinsP, int dataTimeoutSeconds, string publicKeyP, string secretKeyP, string connectorNameP)
        {
            isins = isinsP;
            if (!string.IsNullOrEmpty(publicKeyP))publicKey = publicKeyP;
            if (!string.IsNullOrEmpty(secretKeyP)) secretKey = secretKeyP;
            PublicKey = publicKey;
            Name = connectorNameP;

            if (dataTimeoutSeconds <= 0) return;
            timeoutTimer = new System.Timers.Timer(dataTimeoutSeconds * 1000);
            timeoutTimer.Elapsed += TimeoutTimer_Elapsed;
        }

        public void Start()
        {
            ws = new WebSocket(baseUri) {EnableAutoSendPing = true, AutoSendPingInterval = 1000};

            ws.Opened += Ws_Opened;
            ws.Closed += Ws_Closed;
            ws.Error += Ws_Error;
            ws.MessageReceived += Ws_MessageReceived;
            ws.Open();
        }

        public void Stop()
        {
            timeoutTimer?.Stop();
            ws?.Close();
        }

        public void AddOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            string sideStr = side == OrderSide.Buy ? "buy" : "sell";
            string priceStr = price.ToString(CultureInfo.InvariantCulture);
            string qtyStr = qty.ToString(CultureInfo.InvariantCulture);
            string addOrderMessage = MessageCreator.CreateAddOrderMessage(clientOrderId, isin, sideStr, priceStr, qtyStr, requestId);
            SendRequest(addOrderMessage, Request.AddOrder);
        }

        public void PrepareOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            string sideStr = side == OrderSide.Buy ? "buy" : "sell";
            string priceStr = price.ToString(CultureInfo.InvariantCulture);
            string qtyStr = qty.ToString(CultureInfo.InvariantCulture);
            string addOrderMessage = MessageCreator.CreateAddOrderMessage(clientOrderId, isin, sideStr, priceStr, qtyStr, requestId);
            preparedOrder = addOrderMessage;
        }

        public Task SendPreparedOrder()
        {
            SendRequest(preparedOrder, Request.AddOrder);
            preparedOrder = "";
            return Task.CompletedTask;
        }

        public void CancelOrder(string clientOrderId, int requestId)
        {
            string cancelOrderMessage = MessageCreator.CreateCancelOrderMessage(clientOrderId, requestId);
            SendRequest(cancelOrderMessage, Request.CancelOrder);
        }

        public void ReplaceOrder(string oldClientOrderId, string newClientOrderId, decimal price, decimal qty, int requestId)
        {
            string priceStr = price.ToString(CultureInfo.InvariantCulture);
            string qtyStr = qty.ToString(CultureInfo.InvariantCulture);
            string replaceOrderMessage = MessageCreator.CreateReplaceOrderMessage(oldClientOrderId, newClientOrderId, priceStr, qtyStr, requestId);
            SendRequest(replaceOrderMessage, Request.ReplaceOrder);
        }

        public void GetActiveOrders(int requestId)
        {
            string getActiveOrdersMessage = MessageCreator.CreateGetActiveOrdersMessage(requestId);
            SendRequest(getActiveOrdersMessage, Request.ActiveOrders);
        }

        public void GetPosAndMoney(int requestId)
        {
            string getBalanceMessage = MessageCreator.CreateGetTradingBalanceMessage(requestId);
            SendRequest(getBalanceMessage, Request.TradingBalance);
        }
        #endregion

        #region Websocket handlers
        void Ws_Opened(object sender, EventArgs e)
        {
            lastRequests.Clear(); //чтобы при реконнекте правильно прошли подписки
            string loginMessage = MessageCreator.CreateLoginMessage(publicKey, secretKey);
            SendRequest(loginMessage, Request.Login, asyncRequest: false);

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
            (string type, string payload) = ExtractResponse(e.Message);

            timeoutTimer?.Stop();
            switch (type)
            {
                case "method":
                    string methodType = GetResponseMethod(e.Message);
                    ProcessMethod(methodType, payload);
                    break;

                case "error":
                    ProcessError(payload);
                    break;

                case "result":
                    ProcessResult(payload);
                    break;
            }

            timeoutTimer?.Start();
        }
        #endregion

        #region Processors
        void ProcessMethod(string methodType, string payload)
        {
            switch (methodType)
            {
                case "updateOrderbook":
                    var update = JsonConvert.DeserializeObject<HitBTCBookMessage>(payload);
                    BookUpdateArrived?.Invoke(this, update);
                    break;

                case "snapshotOrderbook":
                    var snapshot = JsonConvert.DeserializeObject<HitBTCBookMessage>(payload);
                    BookSnapshotArrived?.Invoke(this, snapshot);
                    break;

                case "ticker":
                    var ticker = JsonConvert.DeserializeObject<HitBTCTickerMessage>(payload);
                    TickerArrived?.Invoke(this, ticker);
                    break;

                case "activeOrders":
                    //var activeOrders = JsonConvert.DeserializeObject<List<HitBTCOrderMessage>>(payload);
                    //ActiveOrdersListArrived?.Invoke(this, activeOrders.Select(order => (OrderMessage)order).ToList());
                    break;

                case "report":
                    var report = JsonConvert.DeserializeObject<HitBTCOrderMessage>(payload);

                    switch (report.ReportType)
                    {
                        case "new":
                            NewOrderAdded?.Invoke(this, report);
                            break;

                        case "canceled":
                            OrderCanceled?.Invoke(this, report);
                            break;

                        case "replaced":
                            OrderReplaced?.Invoke(this, report);
                            break;

                        case "trade":
                            ExecutionReportArrived?.Invoke(this, report);
                            break;
                    }

                    break;

                default: return;
            }
        }

        void ProcessError(string payload)
        {
            var errorMessage = JsonConvert.DeserializeObject<HitBTCErrorMessage>(payload);
            ErrorSeverity errorSeverity = GetErrorSeverity(errorMessage.Code);

            if (errorSeverity == ErrorSeverity.Fatal || errorSeverity == ErrorSeverity.Unknown)
            {
                var ex = new ExchangeApiException();
                ex.Data["code"] = errorMessage.Code;
                ex.Data["message"] = errorMessage.Message;
                ex.Data["description"] = errorMessage.Description;
                ex.Data["ConnectorName"] = Name;
                throw ex;
            }

            ErrorOccured?.Invoke(this, errorMessage);
        }

        void ProcessResult(string payload)
        {
            if (!lastRequests.TryDequeue(out Request lastRequest))
                throw new ExecutionFlowException($"Received request result, but no request type was stored in the queue before. Result payload: {payload}");

            if (payload == "true") ProcessRequestResult(lastRequest);
            else ProcessRequestResponse(payload, lastRequest);
        }

        void ProcessRequestResult(Request lastRequest)
        {
            switch (lastRequest)
            {
                case Request.Login:
                    string reportSubcribtionMessage = MessageCreator.CreateSubscribeToReportsMessage();
                    SendRequest(reportSubcribtionMessage, Request.ReportSubscribtion, asyncRequest: false);
                    break;

                case Request.ReportSubscribtion:
                    if (isins == null) break;
                    foreach (string isin in isins)
                    {
                        string bookSubscribeMessage = MessageCreator.CreateSubscribeToBookMessage(isin, 1);
                        SendRequest(bookSubscribeMessage, Request.BookSubscribtion, asyncRequest: false);
                    }

                    break;

                case Request.BookSubscribtion:
                    if (isins == null) break;
                    foreach (string isin in isins)
                    {
                        string tickerSubscribeMessage = MessageCreator.CreateSubscribeToTickerMessage(isin, 1);
                        SendRequest(tickerSubscribeMessage, Request.TickerSubscribtion, asyncRequest: false);
                    }

                    break;
            }
        }

        void ProcessRequestResponse(string payload, Request lastRequest)
        {
            try
            {
                switch (lastRequest)
                {
                    case Request.ActiveOrders:
                        payload = payload.Trim(' ');

                        List<HitBTCOrderMessage> activeOrders;
                        //может быть как массив, так и один объект. разделяем по первому символу
                        if (payload.StartsWith('[')) activeOrders = JsonConvert.DeserializeObject<List<HitBTCOrderMessage>>(payload);
                        else if (payload.StartsWith('{'))
                            activeOrders = new List<HitBTCOrderMessage> {JsonConvert.DeserializeObject<HitBTCOrderMessage>(payload)};
                        else throw new SerializationException("Active orders message has unknown beginning.");

                        //Console.WriteLine($"Got active orders for connector {Name}. Websocket handler. {Thread.CurrentThread.ManagedThreadId}");
                        //foreach (HitBTCOrderMessage order in activeOrders)
                        //    Console.WriteLine($"Active order: {order} in connector {Name}. Websocket handler.");
                        ActiveOrdersListArrived?.Invoke(this, activeOrders.Select(order => (OrderMessage)order).ToList());

                        break;

                    case Request.TradingBalance:
                        //сюда может попасть какое-нибудь тормозное сообщение о заявке. поэтому принимаем только массивы.
                        if (payload.StartsWith('{')) return;
                        var balances = JsonConvert.DeserializeObject<List<HitBTCBalanceMessage>>(payload);
                        //Console.WriteLine($"Got balances for connector {Name}. Websocket handler. {Thread.CurrentThread.ManagedThreadId}");
                        //foreach (HitBTCBalanceMessage balance in balances.Where(balance => balance.Available > 0))
                        //    Console.WriteLine($"Balance: {balance} for connector {Name}. Websocket handler.");
                        BalanceArrived?.Invoke(this, balances.Select(order => (BalanceMessage)order).ToList());

                        break;

                    //всё связанное с постановкой/удалением заявок игнорируем, потому что иначе будет дублирование с репортами.
                    //к тому же репорты приходят раньше.
                }
            }
            catch (Exception ex)
            {
                ex.Data["payload"] = payload;
                throw;
            }
        }
        #endregion
    }
}
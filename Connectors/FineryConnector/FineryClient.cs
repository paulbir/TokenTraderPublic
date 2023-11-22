using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using FineryConnector.Model;
using Newtonsoft.Json.Linq;
using SharedDataStructures;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SharedTools;
using WebSocket4Net;
using ErrorEventArgs = SuperSocket.ClientEngine.ErrorEventArgs;

namespace FineryConnector
{
    public class FineryClient : IHedgeConnector
    {
#if DEBUG
            readonly string  wsBaseUrl = "wss://test.finerymarkets.com/ws";
#else
        readonly string wsBaseUrl = "wss://trade.finerymarkets.com/ws";
#endif

        const string bookStreamName = "F";
        readonly decimal globalPriceStep         = 0.00000001m;
        readonly decimal globalQtyStep           = 0.00000001m;
        readonly decimal globalQtyStepMultiplier = 0.00000001m;

        List<string> isins;
        string       secretKey;
        Timer        timeoutTimer;
        WebSocket    ws;

        string preparedOrder = "";

        readonly ConcurrentDictionary<string, IsinData> datasByIsin         = new ConcurrentDictionary<string, IsinData>();
        readonly ConcurrentDictionary<long, IsinData>   datasByFeedId       = new ConcurrentDictionary<long, IsinData>();
        readonly ConcurrentDictionary<int, RequestData> requestDatas        = new ConcurrentDictionary<int, RequestData>();
        readonly Dictionary<long, string>               clientOrderIdByHash = new Dictionary<long, string>();

        public string Name         { get; private set; }
        public string ExchangeName => "finery";
        public string PublicKey    { get; private set; }

        public event EventHandler                        Connected;
        public event EventHandler                        Disconnected;
        public event EventHandler<ErrorMessage>          ErrorOccured;
        public event EventHandler<BookMessage>           BookUpdateArrived;
        public event EventHandler<BookMessage>           BookSnapshotArrived;
        public event EventHandler<TickerMessage>         TickerArrived;
        public event EventHandler<OrderMessage>          NewOrderAdded;
        public event EventHandler<OrderMessage>          OrderCanceled;
        public event EventHandler<OrderMessage>          OrderReplaced;
        public event EventHandler<List<OrderMessage>>    ActiveOrdersListArrived;
        public event EventHandler<OrderMessage>          ExecutionReportArrived;
        public event EventHandler<List<BalanceMessage>>  BalanceArrived;
        public event EventHandler<List<PositionMessage>> PositionArrived;
        public event EventHandler<List<LimitMessage>>    LimitArrived;

        //readonly StreamWriter sw = new StreamWriter($"finery_raw_{DateTime.UtcNow:yyyy-MM-dd HH-mm-ss}.txt") {AutoFlush = true};

        //Timer errorTimer = new Timer(10000);

        public void Init(List<string> isinsP, int dataTimeoutSeconds, string publicKeyP, string secretKeyP, string connectorName)
        {
            isins     = isinsP;
            secretKey = secretKeyP;
            PublicKey = publicKeyP;

            Name = connectorName;

            //errorTimer.Elapsed += ErrorTimer_Elapsed;
            //errorTimer.Start();

            if (dataTimeoutSeconds <= 0) return;
            timeoutTimer         =  new Timer(dataTimeoutSeconds * 1000);
            timeoutTimer.Elapsed += TimeoutTimer_Elapsed;
        }

        //private void ErrorTimer_Elapsed(object sender, ElapsedEventArgs e)
        //{
        //    errorTimer.Stop();
        //    //TODO: Finery test exception
        //    ErrorOccured?.Invoke(this, new FineryErrorMessage(0, "Finery TEST exception", "", 0, true));
        //}

        public void Start()
        {
            ws                 =  new WebSocket(wsBaseUrl) { EnableAutoSendPing = true, AutoSendPingInterval = 30 };
            ws.Opened          += Ws_Opened;
            ws.Closed          += Ws_Closed;
            ws.Error           += Ws_Error;
            ws.MessageReceived += Ws_MessageReceived;

            ws.Open();
        }

        public void Stop()
        {
            DoStop();
        }

        public void AddHedgeOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, decimal slippagePriceFrac, int requestId)
        {
            string addOrderMessage = PrepareAddOrderMessageAndStoreId(clientOrderId, isin, side, price, qty, requestId);
            ws.Send(addOrderMessage);
        }

        public void AddOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            throw new NotImplementedException();
        }

        public void PrepareOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            preparedOrder = PrepareAddOrderMessageAndStoreId(clientOrderId, isin, side, price, qty, requestId);
        }

        public Task SendPreparedOrder()
        {
            if (!string.IsNullOrEmpty(preparedOrder))
            {
                ws.Send(preparedOrder);
                preparedOrder = "";
            }

            return Task.CompletedTask;
        }

        public void CancelOrder(string clientOrderId, int requestId)
        {
            long   clientOrderIdULong        = clientOrderId.GetPositiveStableHashCode();
            string cancelOrderRequestMessage = MessageCreator.CreateCancelOrderRequestMessage(clientOrderIdULong, requestId);
            AddToRequestDatas(requestId, RequestError.CancelOrder, clientOrderId);

            ws.Send(cancelOrderRequestMessage);
        }

        public void ReplaceOrder(string oldClientOrderId, string newClientOrderId, decimal price, decimal qty, int requestId)
        {
            throw new NotImplementedException();
        }

        public void GetActiveOrders(int requestId)
        {
            ActiveOrdersListArrived?.Invoke(this, new List<OrderMessage>());
        }

        public void GetPosAndMoney(int requestId)
        {
            if (ws?.State != WebSocketState.Open)
            {
                ErrorOccured?.Invoke(this,
                                     new FineryErrorMessage(RequestError.TradingBalance,
                                                            "Can't send positions and limits requests because websocket is still not Open.",
                                                            $"{ws?.State}",
                                                            0));
                return;
            }

            string positionsRequestMessage = MessageCreator.CreatePositionsRequestMessage(int.MaxValue - requestId);
            AddToRequestDatas(int.MaxValue - requestId, RequestError.Positions);
            ws.Send(positionsRequestMessage);

            string limitsRequestMessage = MessageCreator.CreateLimitsRequestMessage(requestId);
            AddToRequestDatas(requestId, RequestError.Limits);
            ws.Send(limitsRequestMessage);
        }

        void TimeoutTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            DoStop();
        }

        void DoStop()
        {
            timeoutTimer?.Stop();

            datasByIsin.Clear();
            datasByFeedId.Clear();
            requestDatas.Clear();
            clientOrderIdByHash.Clear();

            if (ws?.State == WebSocketState.Open) ws.Close();
        }

        string PrepareAddOrderMessageAndStoreId(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            long   clientOrderIdULong = clientOrderId.GetPositiveStableHashCode();
            string sideStr            = side == OrderSide.Buy ? "bid" : "ask";
            long   priceLong          = (long)(price / globalPriceStep);
            long   qtyLong            = (long)(qty   / globalQtyStep);

            clientOrderIdByHash.Add(clientOrderIdULong, clientOrderId);
            AddToRequestDatas(requestId, RequestError.AddOrder);

            string addOrderMessage = MessageCreator.CreateAddOrderRequestMessage(isin, clientOrderIdULong, priceLong, qtyLong, sideStr, requestId);
            return addOrderMessage;
        }

        void Ws_Opened(object sender, EventArgs e) { }

        void Ws_Closed(object sender, EventArgs e)
        {
            DoStop();
            Disconnected?.Invoke(this, null);
        }

        void Ws_Error(object sender, ErrorEventArgs e)
        {
            ErrorOccured?.Invoke(this, new FineryErrorMessage(0, "Finery websocket exception", e.Exception.MakeString(), 0, true));
        }

        void Ws_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            //sw.WriteLine($"{DateTime.UtcNow:yy-MM-dd HH:mm:ss.ffff};{e.Message}");
            char firstSymbol = GetFirstMeaningfulSymbol(e.Message);

            if (firstSymbol == '[')
            {
                JArray messageArray = JArray.Parse(e.Message);

                if (TryProcessFeedError(e, messageArray)) return;

                string messageType = (string)messageArray[0];
                switch (messageType)
                {
                    case bookStreamName:
                        ProcessBook(messageArray);
                        break;

                    case "O":
                        ProcessOrder(messageArray);
                        break;

                    case "X":
                        ProcessBackendConnection();
                        break;

                    case "I":
                        ProcessInstruments(messageArray);
                        break;
                }
            }
            else if (firstSymbol == '{')
            {
                JObject messageObject = JObject.Parse(e.Message);

                if (TryProcessRequestResponseError(e, messageObject)) return;

                string @event = (string)messageObject.SelectToken("event");

                switch (@event)
                {
                    case "auth":
                        ProcessAuth();
                        break;
                    case "request":
                        ProcessRequest(messageObject);
                        break;
                }
            }
        }

        bool TryProcessFeedError(MessageReceivedEventArgs e, JArray messageArray)
        {
            if (messageArray.Count < 4) //неизвестный формат
            {
                ErrorOccured?.Invoke(this, new FineryErrorMessage(0, "Unknown message format", e.Message, 0));
                return true;
            }

            if ((string)messageArray[2] == "Z") //возможно ошибка. не ошибка, если это первое сообщение вида ['X', 0, 'Z', 0]
            {
                JToken errorCodeToken = messageArray[3];

                int errorCode;
                if (errorCodeToken.Type == JTokenType.Array) //зачем-то код передаётся массивом
                {
                    errorCode = (int)errorCodeToken[0];
                }
                else errorCode = (int)messageArray[3];

                if (errorCode != 0)
                {
                    ErrorOccured?.Invoke(this, new FineryErrorMessage(0, "", e.Message, errorCode));
                    return true;
                }
            }

            return false;
        }

        bool TryProcessRequestResponseError(MessageReceivedEventArgs e, JObject messageObject)
        {
            if (messageObject.TryGetValue("error", out JToken errorCodeToken) && errorCodeToken != null)
            {
                int errorCode = (int)errorCodeToken;

                messageObject.TryGetValue("param", out JToken errorParamToken);
                string errorParam   = errorParamToken == null ? "" : (string)errorParamToken;
                string errorMessage = string.IsNullOrEmpty(errorParam) ? "" : $"Error in parameter: {errorParam}.";

                if (messageObject.TryGetValue("reqId", out JToken requestIdToken) &&
                    requestIdToken != null                                        &&
                    requestDatas.TryGetValue((int)requestIdToken, out RequestData requestData))
                {
                    ErrorOccured?.Invoke(this,
                                         new FineryErrorMessage(requestData.RequestType,
                                                                errorMessage,
                                                                requestData.ShouldSendOrderIdOnError ? requestData.ClientOrderId : e.Message,
                                                                errorCode));
                }
                else ErrorOccured?.Invoke(this, new FineryErrorMessage(0, errorMessage, e.Message, errorCode));

                return true;
            }

            return false;
        }

        void ProcessBackendConnection()
        {
            string authMessage = MessageCreator.CreateAuthMessage(PublicKey, secretKey);
            ws.Send(authMessage);
        }

        void ProcessAuth()
        {
            Connected?.Invoke(this, null);

            if (!string.IsNullOrEmpty(secretKey))
            {
                string bindToPositionsMessage = MessageCreator.CreateBindToPositionsMessage();
                ws.Send(bindToPositionsMessage);
            }

            if (isins.Count > 0)
            {
                int requestId = 1;
                AddToRequestDatas(requestId, RequestError.Instruments);
                string instrumentsRequestMessage = MessageCreator.CreateInstrumentsRequestMessage(requestId);
                ws.Send(instrumentsRequestMessage);
            }
        }

        void ProcessRequest(JObject messageObject)
        {
            JToken requestIdToken = messageObject.SelectToken("reqId");
            JToken replyToken     = messageObject.SelectToken("reply");
            if (requestIdToken == null || replyToken == null) return;
            int requestId = (int)requestIdToken;

            if (!requestDatas.TryRemove(requestId, out RequestData requestData)) return;

            switch (requestData.RequestType)
            {
                case RequestError.Positions:
                    ProcessPositions(replyToken);
                    break;
                case RequestError.Instruments:
                    ProcessInstruments(replyToken);
                    break;
                case RequestError.Limits:
                    ProcessLimits(replyToken);
                    break;
            }
        }

        void ProcessInstruments(JToken messageToken)
        {
            var messageArray = (JArray)messageToken;

            //пишем в словарь соответствие feedId-qtyStep
            var currencies = messageArray[0].ToObject<List<List<object>>>();
            if (currencies == null) return;
            Dictionary<string, decimal> qtyStepByCurrency = FillQtySteps(currencies);

            //пишем в словари соответствие isin-feedId/qtyStep
            var instruments = messageArray[1].ToObject<List<List<object>>>();
            if (instruments == null) return;
            FillIsinDataDictionaries(instruments, qtyStepByCurrency);

            //подписываемся на исины
            foreach (string isin in isins)
            {
                if (!datasByIsin.TryGetValue(isin, out IsinData isinData))
                {
                    ErrorOccured?.Invoke(this, new FineryErrorMessage(0, $"Got no id for isin {isin} from definitions.", "", 0, true));
                    return;
                }

                string bindToIsinMessage = MessageCreator.CreateBindToIsinMessage(isinData.FeedId, bookStreamName);
                ws.Send(bindToIsinMessage);
            }

            //запускаем таймер на маркет дату. не нужно проверять есть ли исины в массиве, потому что это проверяется перед подпиской на definitions.
            timeoutTimer?.Start();
        }

        void ProcessPositions(JToken messageToken)
        {
            var messageArray = (JArray)messageToken;

            if (messageArray.Count < 2) return;

            var positionsArray      = (JArray)messageArray[1];
            var positionsByCurrency = new Dictionary<string, PositionMessage>();

            foreach (JToken positionToken in positionsArray)
            {
                var singlePosArray = (JArray)positionToken;
                if (singlePosArray.Count < 3) continue;

                string currency = (string)singlePosArray[0];
                if (string.IsNullOrEmpty(currency)) continue;

                decimal qty = (decimal)singlePosArray[1] * globalQtyStep;

                if (positionsByCurrency.TryGetValue(currency, out PositionMessage position)) ((FineryPositionMessage)position).AddQty(qty);
                else
                {
                    position = new FineryPositionMessage(currency, qty);
                    positionsByCurrency.Add(currency, position);
                }
            }

            List<PositionMessage> positions = positionsByCurrency.Values.ToList();
            PositionArrived?.Invoke(this, positions);
        }

        void ProcessLimits(JToken messageToken)
        {
            var messageArray = (JArray)messageToken;

            var balancesByCurrency = new Dictionary<string, BalanceMessage>();
            var limitsByCurrency   = new Dictionary<string, LimitMessage>();

            foreach (JToken limitToken in messageArray)
            {
                var limitArray = (JArray)limitToken;
                if (limitArray.Count < 5) continue;

                string currency = (string)limitArray[0];
                if (string.IsNullOrEmpty(currency)) continue;

                decimal netLimit       = (decimal)limitArray[1] * globalQtyStep;
                decimal grossLimit     = (decimal)limitArray[2] * globalQtyStep;
                decimal netLimitUsed   = (decimal)limitArray[3] * globalQtyStep;
                decimal grossLimitUsed = (decimal)limitArray[4] * globalQtyStep;
                decimal netFree        = netLimit   - netLimitUsed;
                decimal grossFree      = grossLimit - grossLimitUsed;

                TryAddToBalances(balancesByCurrency, currency, grossFree, netFree, grossLimitUsed, netLimitUsed);
                TryAddToLimits(limitsByCurrency, currency, "GROSS", grossLimit, grossLimitUsed);

                //NET лимит может быть отрицательным. это означает, что есть нереализованная прибыль. отрицательный exposure увеличивает free.
                //поэтому будем отрицательные будет считать нулём, чтобы не падать из-за того, что отрицательный exposure < -1 * limit.
                TryAddToLimits(limitsByCurrency, currency, "NET",   netLimit,   Math.Max(netLimitUsed, 0));
            }

            List<LimitMessage> limits = limitsByCurrency.Values.ToList();
            LimitArrived?.Invoke(this, limits);

            List<BalanceMessage> balances = balancesByCurrency.Values.ToList();
            BalanceArrived?.Invoke(this, balances);
        }

        void ProcessBook(JArray messageArray)
        {
            long feedId = (long)messageArray[1];
            if (!datasByFeedId.TryGetValue(feedId, out IsinData isinData)) return;

            var bookSidesArray = (JArray)messageArray[3];
            if (bookSidesArray.Count < 2) return;
            var bidsArray = (JArray)bookSidesArray[0];
            var asksArray = (JArray)bookSidesArray[1];

            string messageType = (string)messageArray[2];
            if (messageType      == "M") ProcessUpdate(isinData, bidsArray, asksArray);
            else if (messageType == "S") ProcessSnapshot(isinData, bidsArray, asksArray);
        }

        void ProcessSnapshot(IsinData isinData, JArray bidsArray, JArray asksArray)
        {
            var snapshot = new FinerySnapshotMessage(isinData.Isin);
            if (!snapshot.SetPriceLevels(OrderSide.Buy,  bidsArray, globalPriceStep, globalQtyStep)) return;
            if (!snapshot.SetPriceLevels(OrderSide.Sell, asksArray, globalPriceStep, globalQtyStep)) return;

            BookSnapshotArrived?.Invoke(this, snapshot);
        }

        void ProcessUpdate(IsinData isinData, JArray bidsArray, JArray asksArray)
        {
            timeoutTimer?.Stop();

            var update = new FineryUpdateMessage(isinData.Isin);
            if (!update.SetPriceLevels(OrderSide.Buy,  bidsArray, globalPriceStep, globalQtyStep)) return;
            if (!update.SetPriceLevels(OrderSide.Sell, asksArray, globalPriceStep, globalQtyStep)) return;

            BookUpdateArrived?.Invoke(this, update);

            timeoutTimer?.Start();
        }

        void ProcessOrder(JArray messageArray)
        {
            string action = (string)messageArray[2];
            if (string.IsNullOrEmpty(action)) return;

            var  executionArray     = (JArray)messageArray[3];
            long clientOrderIdULong = (long)executionArray[5];
            clientOrderIdByHash.TryGetValue(clientOrderIdULong, out string clientOrderId);

            FineryOrderMessage executionWithBaseFields = CreateOrderWithBaseFields(executionArray, clientOrderId, out string errorMessage);
            if (TryProcessOrderParsingErrorMessage(errorMessage, clientOrderId, action)) return;

            switch (action)
            {
                case "+":
                    ProcessAddOrder(executionWithBaseFields);
                    break;

                case "D":
                    ProcessTrade(executionArray, executionWithBaseFields, clientOrderIdULong);
                    break;

                case "-":
                    ProcessCancelOrder(executionWithBaseFields, clientOrderIdULong);
                    break;
            }
        }

        void ProcessAddOrder(FineryOrderMessage order)
        {
            order.SetStatus("Active");
            NewOrderAdded?.Invoke(this, order);
        }

        void ProcessCancelOrder(FineryOrderMessage order, long clientOrderIdULong)
        {
            order.SetStatus("Canceled");
            clientOrderIdByHash.Remove(clientOrderIdULong);
            OrderCanceled?.Invoke(this, order);
        }

        void ProcessTrade(JArray tradeArray, FineryOrderMessage tradeWithBaseFields, long clientOrderIdULong)
        {
            long   sizeLeft = (long)tradeArray[8];
            string status;
            if (sizeLeft <= 0)
            {
                status = "Filled";
                clientOrderIdByHash.Remove(clientOrderIdULong);
            }
            else status = "PartiallyFilled";

            decimal tradePrice = (decimal)tradeArray[13] * globalPriceStep;
            decimal tradeQty   = (decimal)tradeArray[14] * globalQtyStep;

            tradeWithBaseFields.SetStatus(status);
            tradeWithBaseFields.SetForTrade(tradePrice, tradeQty);

            ExecutionReportArrived?.Invoke(this, tradeWithBaseFields);
        }

        char GetFirstMeaningfulSymbol(string message)
        {
            foreach (char element in message)
                if (!char.IsWhiteSpace(element))
                    return element;

            return ' ';
        }

        void AddToRequestDatas(int requestId, RequestError requestType, string clientOrderId = null)
        {
            RequestData requestData = clientOrderId == null ? new RequestData(requestType) : new RequestData(requestType, clientOrderId);
            requestDatas.AddOrUpdate(requestId, requestData, (id, data) => requestData);
        }

        Dictionary<string, decimal> FillQtySteps(List<List<object>> currencies)
        {
            var qtyStepByCurrency = new Dictionary<string, decimal>();
            foreach (List<object> currency in currencies)
            {
                if (currency == null || currency.Count < 4) continue;
                string  currencyName = (string)currency[0];
                long    qtyStepUnits = (long)currency[2];
                decimal qtyStep      = qtyStepUnits * globalQtyStepMultiplier;
                qtyStepByCurrency.TryAdd(currencyName, qtyStep);
            }

            return qtyStepByCurrency;
        }

        void FillIsinDataDictionaries(List<List<object>> instruments, Dictionary<string, decimal> qtyStepByCurrency)
        {
            foreach (List<object> instrument in instruments)
            {
                if (instrument == null || instrument.Count < 4) continue;
                string isin            = (string)instrument[0];
                long   id              = (long)instrument[1];
                string balanceCurrency = (string)instrument[3];

                if (!qtyStepByCurrency.TryGetValue(balanceCurrency, out decimal qtyStep)) continue;
                var isinData = new IsinData(isin, id, qtyStep);
                datasByIsin.TryAdd(isin, isinData);
                datasByFeedId.TryAdd(id, isinData);
            }
        }

        void TryAddToBalances(Dictionary<string, BalanceMessage> balancesByCurrency,
                              string                             currency,
                              decimal                            grossFree,
                              decimal                            netFree,
                              decimal                            grossLimitUsed,
                              decimal                            netLimitUsed)
        {
            decimal available;
            decimal reserved;
            if (grossFree <= netFree)
            {
                available = grossFree;
                reserved  = grossLimitUsed;
            }
            else
            {
                available = netFree;
                reserved  = netLimitUsed;
            }

            if (balancesByCurrency.TryGetValue(currency, out BalanceMessage balance))
            {
                if (available < balance.Available) ((FineryBalanceMessage)balance).Update(available, reserved);
            }
            else balancesByCurrency.Add(currency, new FineryBalanceMessage(currency, available, reserved));
        }

        void TryAddToLimits(Dictionary<string, LimitMessage> limitsByCurrency, string currency, string type, decimal limit, decimal limitUsed)
        {
            string  limitName = $"{currency}_{type}";
            decimal free      = limit - limitUsed;

            if (limitsByCurrency.TryGetValue(limitName, out LimitMessage limitMessageBase))
            {
                var limitMessage = (FineryLimitMessage)limitMessageBase;
                if (free < limitMessage.Free) limitMessage.Update(limitUsed, limit);
            }

            //-1 в качестве min, потому что limitUsed может быть равен 0, и это нормальная ситуация
            else limitsByCurrency.Add(limitName, new FineryLimitMessage(limitName, -1 * limit, limitUsed, limit));
        }

        FineryOrderMessage CreateOrderWithBaseFields(JArray orderArray, string clientOrderId, out string errorMessage)
        {
            errorMessage = "";

            long exchangeOrderIdULong                   = (long)orderArray[4];
            if (exchangeOrderIdULong <= 0) errorMessage += $"Wrong exchange order id={exchangeOrderIdULong};";
            string orderId                              = string.IsNullOrEmpty(clientOrderId) ? exchangeOrderIdULong.ToString() : clientOrderId;

            string isin                                  = (string)orderArray[0];
            if (string.IsNullOrEmpty(isin)) errorMessage += "Empty isin;";

            int       sideInt = (int)orderArray[2];
            OrderSide side    = default;
            if (sideInt      == 0) side =  OrderSide.Buy;
            else if (sideInt == 1) side =  OrderSide.Sell;
            else errorMessage           += $"Wrong order side={sideInt};";

            decimal price = (decimal)orderArray[6] * globalPriceStep;
            decimal qty   = (decimal)orderArray[7] * globalQtyStep;

            long     timestampMs = (long)orderArray[9];
            DateTime timestamp   = DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).UtcDateTime;

            var order = new FineryOrderMessage(orderId, isin, side, price, qty, timestamp);

            return order;
        }

        bool TryProcessOrderParsingErrorMessage(string errorMessage, string clientOrderId, string action)
        {
            if (string.IsNullOrEmpty(errorMessage)) return false;

            RequestError errorCode = default;
            switch (action)
            {
                case "+":
                    errorCode = RequestError.AddOrder;
                    break;

                case "D":
                    errorCode = RequestError.Executions;
                    break;

                case "-":
                    errorCode = RequestError.CancelOrder;
                    break;
            }

            ErrorOccured?.Invoke(this, new FineryErrorMessage(errorCode, errorMessage, clientOrderId, 0, true));
            return true;
        }
    }
}
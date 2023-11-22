using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using CoinFlexConnector.JSONExtensions;
using CoinFlexConnector.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharedDataStructures;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SharedTools;
using SharedTools.Interfaces;
using WebSocket4Net;
using ErrorEventArgs = SuperSocket.ClientEngine.ErrorEventArgs;

namespace CoinFlexConnector
{
    public class CoinFlexClient : ITradeConnector
    {
        readonly decimal scale       = 10000;
        readonly string  wsBaseUri   = "wss://api.coinflex.com/v1";
        readonly string  assetsUrl   = @"https://webapi.coinflex.com/assets/";
        readonly string  balancesUrl = @"https://webapi.coinflex.com/balances/";

        List<string> isins;
        long         userId = -1;
        string       cookie;
        string       passPhrase;

        string restAuthString;

        Timer     timeoutTimer;
        WebSocket ws;

        string storedOrderMessage = "";

        readonly HashSet<long>                             canceledOnTradeOrderIds = new HashSet<long>();
        readonly Dictionary<int, Request>                  requestByTag            = new Dictionary<int, Request>();
        readonly Dictionary<int, string>                   nameById                = new Dictionary<int, string>();
        readonly Dictionary<string, int>                   idByName                = new Dictionary<string, int>();
        readonly Dictionary<string, CoinFlexTickerMessage> tickerByIsin            = new Dictionary<string, CoinFlexTickerMessage>();

        readonly IIdGenerator tagIdGenerator   = new IdGeneratorDecrement();
        readonly IIdGenerator orderIdGenerator = new IdGeneratorOneBased();

        readonly Dictionary<long, string>               clientIdByTonce = new Dictionary<long, string>();
        readonly Dictionary<string, long>               tonceByClientId = new Dictionary<string, long>();
        readonly Dictionary<long, CoinFlexOrderMessage> orderByTonce    = new Dictionary<long, CoinFlexOrderMessage>();

        readonly Dictionary<long, CoinFlexPriceLevel> levelById = new Dictionary<long, CoinFlexPriceLevel>();
        readonly Dictionary<string, CoinFlexBookMessage> inconsistentUpdates = new Dictionary<string, CoinFlexBookMessage>();

        bool sentAuth;
        bool loggedIn;

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

        public string Name         { get; private set; }
        public string ExchangeName => "coinflex";
        public string PublicKey    { get; private set; }

        //ILogger logger;

        //public CoinFlexClient(ILogger logger)
        //{
        //    this.logger = logger;
        //}

        public void Init(List<string> isinsP, int dataTimeoutSeconds, string publicKeyP, string secretKeyP, string connectorName)
        {
            isins = isinsP;

            if (!string.IsNullOrEmpty(publicKeyP) && !string.IsNullOrEmpty(secretKeyP))
            {
                userId = publicKeyP.ToLong();

                string[] keyTokens = secretKeyP.Split('_');
                if (keyTokens.Length < 2) throw new ConfigErrorsException($"Secret key must have format Cookie_Passphrase. Got {secretKeyP} instead.");

                cookie     = keyTokens[0];
                passPhrase = keyTokens[1];

                string rawAuthStr   = $"{publicKeyP}/{cookie}:{passPhrase}";
                byte[] rawAuthBytes = Encoding.UTF8.GetBytes(rawAuthStr);
                restAuthString = Convert.ToBase64String(rawAuthBytes);
            }

            PublicKey = publicKeyP;

            Name = connectorName;

            //if (Name == "data_trade") sw = new StreamWriter("raw"){AutoFlush = true};

            if (dataTimeoutSeconds <= 0) return;
            timeoutTimer         =  new Timer(dataTimeoutSeconds * 1000);
            timeoutTimer.Elapsed += TimeoutTimer_Elapsed;
        }

        public void Start()
        {
            GetAssets();

            ws                 =  new WebSocket(wsBaseUri) {EnableAutoSendPing = true, AutoSendPingInterval = 45000};
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

        public void AddOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            if (TryPrepareOrderMessage(clientOrderId, isin, side, price, qty, requestId, out string placeOrderMessage)) ws.Send(placeOrderMessage);
        }

        public void PrepareOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            TryPrepareOrderMessage(clientOrderId, isin, side, price, qty, requestId, out storedOrderMessage);
        }

        public Task SendPreparedOrder()
        {
            if (!string.IsNullOrEmpty(storedOrderMessage)) ws.Send(storedOrderMessage);
            return Task.CompletedTask;
        }

        public void CancelOrder(string clientOrderId, int requestId)
        {
            if (!tonceByClientId.TryGetValue(clientOrderId, out long tonce))
            {
                ErrorOccured?.Invoke(this,
                                     new CoinFlexErrorMessage((int)RequestError.CancelOrder,
                                                              $"Tonce was not found for ClientOrderId={clientOrderId}.",
                                                              clientOrderId));
                return;
            }

            StoreRequest(RequestType.CancelOrder, requestId, clientOrderId);
            string cancelOrderMessage = MessageCreator.CreateCancelOrderMessage(requestId, tonce);

            ws.Send(cancelOrderMessage);
        }

        public void ReplaceOrder(string oldClientOrderId, string newClientOrderId, decimal price, decimal qty, int requestId)
        {
            throw new NotImplementedException();
        }

        public void GetActiveOrders(int requestId)
        {
            StoreRequest(RequestType.ActiveOrders, requestId);
            string getOrdersMessage = MessageCreator.CreateGetOrdersMessage(requestId);
            ws.Send(getOrdersMessage);
        }

        public void GetPosAndMoney(int requestId)
        {
            //StoreRequest(RequestType.Balances, requestId);
            //string getBalancesMessage = MessageCreator.CreateBalancesMessage(requestId);
            //ws.Send(getBalancesMessage);
            string balancesStr = QueryString(balancesUrl);

            if (string.IsNullOrEmpty(balancesStr))
                ErrorOccured?.Invoke(this, new CoinFlexErrorMessage((int)RequestError.TradingBalance, "Got empty balances string", ""));

            var balances = JsonConvert.DeserializeObject<List<CoinFlexBalanceMessage>>(balancesStr);
            foreach (CoinFlexBalanceMessage balance in balances) balance.SetValues(scale, nameById);

            BalanceArrived?.Invoke(this, balances.Select(balance => (BalanceMessage)balance).ToList());
        }

        void TimeoutTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            DoStop();
            Disconnected?.Invoke(this, null);
        }

        void DoStop()
        {
            canceledOnTradeOrderIds.Clear();
            requestByTag.Clear();
            nameById.Clear();
            idByName.Clear();
            tickerByIsin.Clear();
            clientIdByTonce.Clear();
            tonceByClientId.Clear();
            orderByTonce.Clear();
            levelById.Clear();
            inconsistentUpdates.Clear();

            timeoutTimer?.Stop();
            if (ws?.State == WebSocketState.Open) ws.Close();

            sentAuth = false;
            loggedIn = false;
        }

        void Ws_Opened(object sender, EventArgs e) { }

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
            JObject messageObject = JObject.Parse(e.Message);

            if (!loggedIn && !sentAuth && CheckWelcome(messageObject))
            {
                ProcessWelcome(messageObject);
                return;
            }

            bool hasTag = TryRemoveRequestByTag(messageObject, out Request request);
            if (TryProcessError(messageObject, request)) return;

            if (sentAuth && !loggedIn)
            {
                ProcessLogonResponse(hasTag, request, e);
                return;
            }

            if (hasTag) ProcessRequestResponse(messageObject, request);
            else
            {
                //logger.Enqueue(e.Message);
                ProcessStreamUpdate(messageObject);
            }
        }

        void ProcessWelcome(JObject messageObject)
        {
            if (userId > 0) //торговый
            {
                SendAuth(messageObject);
                sentAuth = true;
            }
            else //только данные
            {
                loggedIn = true;
                if (isins != null && isins.Count > 0)
                {
                    SubscribeToIsins();
                    timeoutTimer.Start();
                }

                Connected?.Invoke(this, null);
            }
        }

        void ProcessLogonResponse(bool hasTag, Request request, MessageReceivedEventArgs e)
        {
            if (!hasTag || request == null)
                throw new ExecutionFlowException($"Expected logon response, but couldn't get request by tag from it. Response: {e.Message}");

            if (request.RequestType != RequestType.Authentication)
                throw new ExecutionFlowException($"Unexpected request type {request.RequestType} for authentication response.");

            loggedIn = true;
            if (isins != null && isins.Count > 0)
            {
                SubscribeToIsins();
                timeoutTimer.Start();
            }

            //1. есть cancelAll, можно всё дополнительно поснимать при старте.
            //2. в доках написано, что tonce должен быть неубывающим после последнего вызова cancelAll. поэтому сбрасываем последний tonce вызовом.
            StoreRequest("", RequestType.CancelOrder, out int id);
            string cancelAllOrdersMessage = MessageCreator.CreateCancelAllOrdersMessage(id);
            ws.Send(cancelAllOrdersMessage);

            Connected?.Invoke(this, null);
        }

        void ProcessRequestResponse(JObject messageObject, Request request)
        {
            switch (request.RequestType)
            {
                case RequestType.Orders:
                    ProcessBookSnapshot(messageObject, request.Isin);
                    break;

                case RequestType.Ticker:
                    ProcessTickerSnapshot(messageObject, request.Isin);
                    break;

                case RequestType.ActiveOrders:
                    ProcessActiveOrders(messageObject);
                    break;

                default: return;
            }
        }

        void ProcessStreamUpdate(JObject messageObject)
        {
            timeoutTimer.Stop();

            string notice = (string)messageObject.SelectToken("notice");

            if (string.IsNullOrEmpty(notice))
            {
                ErrorOccured?.Invoke(this, new CoinFlexErrorMessage(0, $"Notice is empty: {notice} for stream update.", messageObject.ToString()));
                return;
            }

            switch (notice)
            {
                case "OrderOpened":
                    ProcessOrdersUpdate(messageObject, OrderUpdateType.Opened);
                    break;

                case "OrderClosed":
                    ProcessOrdersUpdate(messageObject, OrderUpdateType.Closed);
                    break;

                case "OrderModified":
                    ProcessOrdersUpdate(messageObject, OrderUpdateType.Modified);
                    break;

                case "OrdersMatched":
                    ProcessOrdersUpdate(messageObject, OrderUpdateType.Matched);
                    break;

                case "TickerChanged":
                    ProcessTickerUpdate(messageObject);
                    break;
            }

            timeoutTimer.Start();
        }

        void ProcessTickerSnapshot(JObject messageObject, string snapshotIsin)
        {
            var tickerSnapshot = messageObject.ToObject<CoinFlexTickerMessage>();
            tickerSnapshot.SetPriceQty(scale);
            tickerSnapshot.SetIsin(snapshotIsin);

            tickerByIsin[snapshotIsin] = tickerSnapshot;

            TickerArrived?.Invoke(this, tickerSnapshot);
        }

        void ProcessTickerUpdate(JObject messageObject)
        {
            int baseId    = (int)messageObject.SelectToken("base");
            int counterId = (int)messageObject.SelectToken("counter");
            if (!nameById.TryGetValue(baseId,    out string baseName)) return;
            if (!nameById.TryGetValue(counterId, out string counterName)) return;
            string isin = $"{baseName}.{counterName}";

            if (!tickerByIsin.TryGetValue(isin, out CoinFlexTickerMessage ticker)) return;

            if (messageObject.SelectToken("bid")?.ToObject<decimal>() is decimal bid) ticker.UpdateBid(bid     / scale);
            if (messageObject.SelectToken("ask")?.ToObject<decimal>() is decimal ask) ticker.UpdateAsk(ask     / scale);
            if (messageObject.SelectToken("last")?.ToObject<decimal>() is decimal last) ticker.UpdateLast(last / scale);

            TickerMessage tickerToSend = ticker.MakeDeepCopy();

            TickerArrived?.Invoke(this, tickerToSend);
        }

        void ProcessBookSnapshot(JObject messageObject, string snapshotIsin)
        {
            var snapshot = messageObject.SelectToken("orders").ToObject<List<RawOrderUpdate>>();

            foreach (RawOrderUpdate sntUpdate in snapshot)
                levelById.Add(sntUpdate.ExchangeOrderId, new CoinFlexPriceLevel(sntUpdate.PriceUnscaled, sntUpdate.QtyUnscaled, scale, true));

            var book = new CoinFlexBookMessage(snapshotIsin, snapshot, scale);

            BookSnapshotArrived?.Invoke(this, book);
        }

        void ProcessOrdersUpdate(JObject messageObject, OrderUpdateType updateType)
        {
            if (updateType == OrderUpdateType.Matched)
            {
                var update = messageObject.ToObject<RawOrdersMatched>();

                string isin = GetIsinByIds(update.BaseId, update.CounterId);

                if (update.BidTonce.IsSet)
                    ProcessMyExecutionReport(messageObject,
                                             isin,
                                             OrderSide.Buy,
                                             update.BidTonce,
                                             update.BidOrderId,
                                             update.BidQtyLeftUnscaled,
                                             update.PriceUnscaled,
                                             update.TradeQtyUnscaled,
                                             update.BidCounterFee,
                                             update.TimestampTicks);

                if (update.AskTonce.IsSet)
                    ProcessMyExecutionReport(messageObject,
                                             isin,
                                             OrderSide.Sell,
                                             update.AskTonce,
                                             update.AskOrderId,
                                             update.AskQtyLeftUnscaled,
                                             update.PriceUnscaled,
                                             update.TradeQtyUnscaled,
                                             update.AskCounterFee,
                                             update.TimestampTicks);

                ProcessBookTrade(update, isin);
            }
            else
            {
                var update = messageObject.ToObject<RawOrderUpdate>();

                if (update.Tonce.IsSet) ProcessMyOrderUpdate(update, update.Tonce.HasValue ? update.Tonce.Value : 0, updateType);

                ProcessBookUpdate(update, updateType);
            }
        }

        void ProcessMyOrderUpdate(RawOrderUpdate update, long tonce, OrderUpdateType updateType)
        {
            string isin = GetIsinByIds(update.BaseId, update.CounterId);

            if (!clientIdByTonce.TryGetValue(tonce, out string clientOrderId)) clientOrderId = update.ExchangeOrderId.ToString();

            var order = new CoinFlexOrderMessage(clientOrderId,
                                                 isin,
                                                 updateType.ToString(),
                                                 update.PriceUnscaled,
                                                 update.QtyUnscaled,
                                                 update.TimestampTicks,
                                                 scale);

            switch (updateType)
            {
                case OrderUpdateType.Opened:
                    orderByTonce.Add(tonce, order);
                    NewOrderAdded?.Invoke(this, order);
                    break;

                case OrderUpdateType.Modified:
                    ErrorOccured?.Invoke(this, new CoinFlexErrorMessage(0, "Received unexpected Modified update for order", order.ToString()));
                    break;

                case OrderUpdateType.Closed:
                    clientIdByTonce.Remove(tonce);
                    tonceByClientId.Remove(clientOrderId);

                    if (orderByTonce.Remove(tonce, out CoinFlexOrderMessage storedOrder)) order.UpdateTimestamp(storedOrder.Timestamp);

                    if (canceledOnTradeOrderIds.Remove(update.ExchangeOrderId)) return;
                    OrderCanceled?.Invoke(this, order);
                    break;
            }
        }

        void ProcessBookUpdate(RawOrderUpdate update, OrderUpdateType updateType)
        {
            var       updatedLevel = new CoinFlexPriceLevel(update.PriceUnscaled, update.QtyUnscaled, scale, true);
            string    isin         = GetIsinByIds(update.BaseId, update.CounterId);
            OrderSide side         = update.QtyUnscaled > 0 ? OrderSide.Buy : OrderSide.Sell;

            CoinFlexBookMessage bookUpdate;

            if (!tickerByIsin.TryGetValue(isin, out CoinFlexTickerMessage ticker))
            {
                ErrorOccured?.Invoke(this, new CoinFlexErrorMessage(0, $"Isin {isin} was not found in ticker dictionary", ""));
                return;
            }

            if (!inconsistentUpdates.TryGetValue(isin, out CoinFlexBookMessage storedUpdate))
            {
                ErrorOccured?.Invoke(this, new CoinFlexErrorMessage(0, $"Isin {isin} was not found in inconsistent updates dictionary", ""));
                return;
            }

            switch (updateType)
            {
                case OrderUpdateType.Opened: //просто добавляем новый уровень
                    bookUpdate = new CoinFlexBookMessage(isin, side, updatedLevel.DeepCopy());
                    levelById.Add(update.ExchangeOrderId, updatedLevel);
                    break;

                case OrderUpdateType.Modified: //изменение
                    if (!levelById.TryGetValue(update.ExchangeOrderId, out CoinFlexPriceLevel storedLevel))
                    {
                        //если старый уровень не найден, то просто добавляем этот уровень
                        bookUpdate = new CoinFlexBookMessage(isin, side, updatedLevel.DeepCopy());
                    }
                    else //если этот уровень уже был добавлен
                    {
                        if (storedLevel.Price != updatedLevel.Price) //если цена изменилась
                        {
                            //удаляем со старой ценой, старым количеством и добавляем с новой ценой и количеством
                            bookUpdate = new CoinFlexBookMessage(isin, side, storedLevel.DecreasingLevel());
                            bookUpdate.AccumulateUpdate(side, updatedLevel.DeepCopy());
                        }
                        else //цена не менялась
                        {
                            if (updatedLevel.Qty == storedLevel.Qty) return; //если и количество не менялось тоже, то ошибка. выходим.

                            //иначе добавляем уровень с разницей количеств
                            bookUpdate = new CoinFlexBookMessage(isin, side, CoinFlexPriceLevel.CreateDiffLevel(updatedLevel, storedLevel));
                        }
                    }

                    levelById[update.ExchangeOrderId] = updatedLevel;
                    break;

                case OrderUpdateType.Closed:

                    if (canceledOnTradeOrderIds.Remove(update.ExchangeOrderId)) return; //уровень был удалён в обработчике сделки. выходим.

                    //просто удаляем пришедший уровень
                    bookUpdate = new CoinFlexBookMessage(isin, side, updatedLevel.DecreasingLevel());
                    levelById.Remove(update.ExchangeOrderId);
                    break;
                default: return;
            }

            //при добавлении заявки, сносящей противоположную сторону, стакан становится неконсистентным. не отправляем такой апдейст.
            //сохраняем его до сделки, где всё должно исправиться.
            if(storedUpdate.IsEmpty) TryStoreInconsistentUpdate(storedUpdate, bookUpdate, ticker);

            //если проблем не выявилось, то сохранено ничего не будет. можно отправлять.
            if (storedUpdate.IsEmpty)
            {
                //logger.Enqueue("Update: " + bookUpdate);
                BookUpdateArrived?.Invoke(this, bookUpdate);
            }
            //else logger.Enqueue("Stored: " + bookUpdate);
        }

        void TryStoreInconsistentUpdate(CoinFlexBookMessage storedUpdate, CoinFlexBookMessage update, CoinFlexTickerMessage ticker)
        {

            foreach (PriceLevel level in update.Bids)
            {
                if (Math.Abs(level.Price) >= ticker.Ask && level.Qty > 0)
                {
                    storedUpdate.AccumulateUpdates(update);
                    return;
                }
            }

            foreach (PriceLevel level in update.Asks)
            {
                if (Math.Abs(level.Price) <= ticker.Bid && level.Qty > 0)
                {
                    storedUpdate.AccumulateUpdates(update);
                    return;
                }
            }
        }

        void ProcessMyExecutionReport(JObject        messageObject,
                                      string         isin,
                                      OrderSide      side,
                                      Settable<long> tonce,
                                      long           exchangeOrderId,
                                      decimal        qtyLeftUnscaled,
                                      decimal        tradePriceUnscaled,
                                      decimal        tradeQtyUnscaled,
                                      decimal        feeUnscaled,
                                      long           timestamp)
        {
            if (qtyLeftUnscaled == 0) canceledOnTradeOrderIds.Add(exchangeOrderId); //снесли полностью

            CoinFlexOrderMessage executionReport;
            if (!tonce.HasValue)
            {
                executionReport = new CoinFlexOrderMessage(exchangeOrderId.ToString(),
                                                           isin,
                                                           side,
                                                           OrderUpdateType.Matched.ToString(),
                                                           tradePriceUnscaled,
                                                           qtyLeftUnscaled,
                                                           timestamp,
                                                           tradeQtyUnscaled,
                                                           feeUnscaled,
                                                           scale);
                ExecutionReportArrived?.Invoke(this, executionReport);
                return;
            }

            if (!orderByTonce.TryGetValue(tonce, out CoinFlexOrderMessage storedOrder))
            {
                ErrorOccured?.Invoke(this,
                                     new CoinFlexErrorMessage((int)RequestError.Executions,
                                                              $"Tonce {tonce.Value} was not found in dictionary.",
                                                              messageObject.ToString()));
                return;
            }

            if (qtyLeftUnscaled == 0) //снесли полностью
            {
                orderByTonce.Remove(tonce);
                if (clientIdByTonce.Remove(tonce, out string clientOrderId)) tonceByClientId.Remove(clientOrderId);
            }

            executionReport = storedOrder.CreateExecutionReport(tradePriceUnscaled, tradeQtyUnscaled, feeUnscaled, scale);
            ExecutionReportArrived?.Invoke(this, executionReport);
        }

        void ProcessBookTrade(RawOrdersMatched update, string isin)
        {
            CoinFlexBookMessage bookUpdate = null;

            ProcessChangedOneSideOnTrade(isin, OrderSide.Buy,  update.BidOrderId, update.BidQtyLeftUnscaled, ref bookUpdate);
            ProcessChangedOneSideOnTrade(isin, OrderSide.Sell, update.AskOrderId, update.AskQtyLeftUnscaled, ref bookUpdate);

            //если были сохранены неконсистентные апдейты, то вставляем их перед апдейтами со сделкой. чтобы в стакане всё применилось сразу и не было кроссов.
            if (inconsistentUpdates.TryGetValue(isin, out CoinFlexBookMessage storedUpdate) && !storedUpdate.IsEmpty)
            {
                storedUpdate.AccumulateUpdates(bookUpdate);
                bookUpdate = storedUpdate.CreateDeepCopy();
                storedUpdate.Clear();
            }
            //logger.Enqueue("Trade: " + bookUpdate);
            BookUpdateArrived?.Invoke(this, bookUpdate);

            void ProcessChangedOneSideOnTrade(string                  isinLocal,
                                              OrderSide               side,
                                              long                    sideExchangeOrderId,
                                              long                    qtyLeftUnscaled,
                                              ref CoinFlexBookMessage localBookUpdate)
            {
                //заявка изменилась и была добавлена ранее
                if (sideExchangeOrderId > 0 && levelById.TryGetValue(sideExchangeOrderId, out CoinFlexPriceLevel storedLevel))
                {
                    CoinFlexPriceLevel levelToSend;

                    if (qtyLeftUnscaled == 0) //снесли целиком
                    {
                        //добавляем в сет, чтобы не обрабатывать последующее сообщение об удалении
                        canceledOnTradeOrderIds.Add(sideExchangeOrderId);

                        levelById.Remove(sideExchangeOrderId);

                        //апдейт ранее был добавлен. создаём левел для удаления заявки.
                        levelToSend = storedLevel.DecreasingLevel();
                    }
                    else //что-то в заявке осталось
                    {
                        //левел с оставшейся заявкой. цену берём из запомненной заявки. объём из остатка после сделки.
                        var updateLevel = storedLevel.DeepCopy();
                        updateLevel.UpdateQty(qtyLeftUnscaled, scale, true);

                        //создаём левел для уменьшения количества на уровне на изменение количества в заявке
                        levelToSend                    = CoinFlexPriceLevel.CreateDiffLevel(updateLevel, storedLevel);
                        levelById[sideExchangeOrderId] = updateLevel;
                    }

                    if (localBookUpdate == null) localBookUpdate = new CoinFlexBookMessage(isinLocal, side, levelToSend);
                    else localBookUpdate.AccumulateUpdate(side, levelToSend);
                }
            }
        }

        void ProcessActiveOrders(JObject messageObject)
        {
            var orders = messageObject.SelectToken("orders").ToObject<List<RawOrderUpdate>>();

            if (orders == null)
            {
                ActiveOrdersListArrived?.Invoke(this, new List<OrderMessage>());
                return;
            }

            var activeOrders = new List<OrderMessage>();
            foreach (RawOrderUpdate rawOrder in orders)
            {
                string isin = GetIsinByIds(rawOrder.BaseId, rawOrder.CounterId);

                string clientOrderId = null;
                if (rawOrder.Tonce.HasValue && !clientIdByTonce.TryGetValue(rawOrder.Tonce, out clientOrderId))
                {
                    clientOrderId = rawOrder.ExchangeOrderId.ToString();
                    clientIdByTonce.Add(rawOrder.Tonce, clientOrderId);
                    tonceByClientId.Add(clientOrderId, rawOrder.Tonce);
                }

                var order = new CoinFlexOrderMessage(clientOrderId,
                                                     isin,
                                                     OrderUpdateType.Opened.ToString(),
                                                     rawOrder.PriceUnscaled,
                                                     rawOrder.QtyUnscaled,
                                                     rawOrder.TimestampTicks,
                                                     scale);
                if (rawOrder.Tonce.HasValue && !orderByTonce.ContainsKey(rawOrder.Tonce)) orderByTonce.Add(rawOrder.Tonce, order);

                activeOrders.Add(order);
            }

            ActiveOrdersListArrived?.Invoke(this, activeOrders);
        }

        bool TryPrepareOrderMessage(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId, out string placeOrderMessage)
        {
            placeOrderMessage = "";

            string[] isinParts   = isin.Split('.');
            string   baseName    = isinParts[0];
            string   counterName = isinParts[1];

            if (!idByName.TryGetValue(baseName, out int baseId) || !idByName.TryGetValue(counterName, out int counterId))
            {
                ErrorOccured?.Invoke(this,
                                     new CoinFlexErrorMessage((int)RequestError.AddOrder, $"Couldn't find {baseName} or {counterName} in dictionary.", ""));
                return false;
            }

            long qtyUnscaled                        = (long)(qty * scale);
            if (side == OrderSide.Sell) qtyUnscaled = -1 * qtyUnscaled;
            long priceUnscaled                      = (long)(price * scale);

            StoreRequest(RequestType.AddOrder, requestId, clientOrderId);
            long tonce = orderIdGenerator.Id;
            clientIdByTonce.Add(tonce, clientOrderId);
            tonceByClientId.Add(clientOrderId, tonce);
            placeOrderMessage = MessageCreator.CreatePlaceOrderMessage(requestId, tonce, baseId, counterId, qtyUnscaled, priceUnscaled);

            return true;
        }

        void SubscribeToIsins()
        {
            foreach (string isin in isins)
            {
                string[] tokens = isin.Split('.');
                if (tokens.Length < 2) throw new ConfigErrorsException("Wrong isin format.");
                string baseName    = tokens[0];
                string counterName = tokens[1];

                if (!idByName.TryGetValue(baseName, out int baseId)) throw new ExchangeApiException($"Base currency {baseName} was not found in exchange.");

                if (!idByName.TryGetValue(counterName, out int counterId))
                    throw new ExchangeApiException($"Counter currency {counterName} was not found in exchange.");

                inconsistentUpdates.Add(isin, new CoinFlexBookMessage(isin));

                SendSubscribeMessage(baseId, counterId, isin, MessageCreator.CreateSubscribeWatchOrdersMessage, RequestType.Orders);
                SendSubscribeMessage(baseId, counterId, isin, MessageCreator.CreateSubscribeWatchTickerMessage, RequestType.Ticker);
            }

            void SendSubscribeMessage(long baseId, long counterId, string isin, Func<int, long, long, string> subscribeMessageCreator, RequestType requestType)
            {
                StoreRequest(isin, requestType, out int id);
                string subscribeMessage = subscribeMessageCreator(id, baseId, counterId);

                ws.Send(subscribeMessage);
            }
        }

        bool CheckWelcome(JObject messageObject)
        {
            JToken noticeToken = messageObject.SelectToken("notice");
            if (noticeToken != null)
            {
                string notice = (string)noticeToken;
                if (notice == "Welcome") return true;
            }

            return false;
        }

        void SendAuth(JObject messageObject)
        {
            string serverNonce = (string)messageObject.SelectToken("nonce");
            (string clientNonce, string r, string s) = Cryptography.CreateAuthStrings(userId, passPhrase, serverNonce);

            StoreRequest("", RequestType.Authentication, out int id);
            string authenticateMessage = MessageCreator.CreateAuthenticateMessage(id, userId, cookie, clientNonce, r, s);
            ws.Send(authenticateMessage);
        }

        bool TryProcessError(JObject messageObject, Request request)
        {
            JToken errorCodeToken = messageObject.SelectToken("error_code");
            if (errorCodeToken == null) return false;

            int errorCode = (int)errorCodeToken;
            if (errorCode > 0)
            {
                string errorMsg = (string)messageObject.SelectToken("error_msg");

                if (request != null && request.IsRequestToThrowOnError)
                {
                    throw new ConfigErrorsException($"Fatal error after request {request.RequestType} for isin {request.Isin}. " +
                                                    $"ErrorCode={errorCode}. Error message: {errorMsg}.");
                }

                if (request == null) throw new ConfigErrorsException($"Fatal error for unknown request. ErrorCode={errorCode}. Error message: {errorMsg}.");

                int    myErrorCode = ErrorCodeFromRequestType(request.RequestType);
                string description = request.RequestType == RequestType.CancelOrder ? request.ClientOrderId : messageObject.ToString();

                ErrorOccured?.Invoke(this, new CoinFlexErrorMessage(myErrorCode, $"Error. Id={errorCode}. Message={errorMsg}.", description));
                return true;
            }

            return false;
        }

        bool TryRemoveRequestByTag(JObject messageObject, out Request request)
        {
            request = default;
            JToken tagToken = messageObject.SelectToken("tag");

            if (tagToken == null) return false;

            int tag = (int)tagToken;

            if (!requestByTag.Remove(tag, out request)) throw new ExecutionFlowException($"Could not find requestId={tag} in dictionary.");

            return true;
        }

        void GetAssets()
        {
            string assetsResponse = QueryString(assetsUrl);
            if (string.IsNullOrEmpty(assetsResponse)) throw new ExchangeApiException("Couldn't retrieve assets.");

            var assets = JsonConvert.DeserializeObject<List<Asset>>(assetsResponse);
            foreach (Asset asset in assets)
            {
                nameById.Add(asset.Id, asset.Name);
                idByName.Add(asset.Name, asset.Id);
            }
        }

        string QueryString(string requestStr, RequestError requestError = default)
        {
            HttpWebRequest request = WebRequest.CreateHttp(requestStr);
            request.Headers["Content-type"]  = "application/x-www-form-urlencoded";
            request.Headers["authorization"] = $"Basic {restAuthString}";

            WebResponse response = null;
            try { response = request.GetResponse(); }
            catch (WebException ex)
            {
                using (var exResponse = (HttpWebResponse)ex.Response)
                {
                    using (var sr = new StreamReader(exResponse.GetResponseStream()))
                    {
                        string responseString = sr.ReadToEnd();

                        if (requestError == default)
                        {
                            ex.Data["ResponseString"] = responseString;
                            throw;
                        }

                        ErrorOccured?.Invoke(this,
                                             new CoinFlexErrorMessage((int)requestError,
                                                                      $"Request failed with error: {responseString}.",
                                                                      $"Request: {requestStr}."));
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

        void StoreRequest(string isin, RequestType requestType, out int id)
        {
            id = tagIdGenerator.Id;
            requestByTag.Add(id, new Request(isin, requestType));
        }

        void StoreRequest(RequestType requestType, int id)
        {
            requestByTag.Add(id, new Request(requestType));
        }

        void StoreRequest(RequestType requestType, int id, string clientOrderId)
        {
            requestByTag.Add(id, new Request(requestType, clientOrderId));
        }

        string GetIsinByIds(int baseId, int counterId)
        {
            if (!nameById.TryGetValue(baseId,    out string baseName)) return "";
            if (!nameById.TryGetValue(counterId, out string counterName)) return "";
            return $"{baseName}.{counterName}";
        }

        int ErrorCodeFromRequestType(RequestType type)
        {
            switch (type)
            {
                case RequestType.Balances:     return (int)RequestError.TradingBalance;
                case RequestType.ActiveOrders: return (int)RequestError.ActiveOrders;
                case RequestType.AddOrder:     return (int)RequestError.AddOrder;
                case RequestType.CancelOrder:  return (int)RequestError.CancelOrder;
                default:                       return 0;
            }
        }
    }
}
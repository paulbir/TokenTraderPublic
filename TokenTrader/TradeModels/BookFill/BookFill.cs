using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Timers;
using Nito.AsyncEx;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SharedTools;
using SharedTools.Interfaces;
using TokenTrader.Initialization;
using TokenTrader.Interfaces;
using TokenTrader.OrderBook;
using TokenTrader.State;
using Timer = System.Timers.Timer;

namespace TokenTrader.TradeModels.BookFill
{
    partial class BookFill : ITradeModel
    {
        const int DataTimeoutSeconds              = 180;
        const int WaitingTimeoutMs                = 5000;
        const int BalancesRefreshingTimeoutMs     = 10000;
        const int ReconnectOnBrokenBookTimeoutSec = 300;
        //const int StopOnStuckBookTimeoutSec       = 300;
        const int WaitPendingTimeoutMs            = 3000;

        const int NumBookLevelsToSendUdp       = 5;
        const int BookErrorQueueWindowMs       = 10000;
        const int NumBookErrorsInWindowToThrow = 10;

        //const double OrderQtySigmaFrac = 0.3;
        const double  OrderDistanceMinstepsSigmaFrac    = 0.3;
        const double  OrdersNextActionDelaySigmaFrac    = 0.3;
        const double  OrderToEdgeDistanceToleranceCoef  = 1.5;
        const decimal ChangedOrderPriceRegionShrinkCoef = 0.33m;
        const decimal FullVolToleranceCoef              = 0.6m;

        readonly BookFillContext         context;
        readonly CancellationTokenSource childSource;
        readonly IIdGenerator            requestIdGenerator;
        readonly IIdGenerator            orderIdGenerator;
        readonly ILogger                 logger;
        readonly IUdpSender              udpSender;
        readonly IUdpReceiver            udpReceiver;

        readonly ConcurrentDictionary<string, AccountState>          accountStates              = new ConcurrentDictionary<string, AccountState>();
        readonly ConcurrentDictionary<string, bool>                  areDataConnectorsConnected = new ConcurrentDictionary<string, bool>();
        readonly ConcurrentDictionary<string, IsinMMState>           mmStates                   = new ConcurrentDictionary<string, IsinMMState>();
        readonly ConcurrentDictionary<string, IsinMMState>           mmStatesByHedgeIsin        = new ConcurrentDictionary<string, IsinMMState>();
        readonly ConcurrentDictionary<string, VariableData>          variables                  = new ConcurrentDictionary<string, VariableData>();
        readonly ConcurrentDictionary<string, PricesState>           utilityPricesStates        = new ConcurrentDictionary<string, PricesState>();
        readonly ConcurrentDictionary<string, UsedAddOrderPriceData> usedPricesDatas            = new ConcurrentDictionary<string, UsedAddOrderPriceData>();
        readonly ConcurrentDictionary<string, List<string>>          isinsByConnector           = new ConcurrentDictionary<string, List<string>>();

        readonly ConcurrentDictionary<string, (decimal price, decimal qty)> preparedRandomizeOrdersByCancelId =
            new ConcurrentDictionary<string, (decimal price, decimal qty)>();

        readonly ActionBlock<VariableData> variableMessageProcessor;
        readonly ActionBlock<OrderMessage> tradeHedger;
        readonly ActionBlock<string>       udpMessageProcessor;
        readonly HashSet<string>           usedCurrencies = new HashSet<string>();

        readonly AsyncManualResetEvent connectedResetEvent = new AsyncManualResetEvent();
        readonly AsyncManualResetEvent exitStartResetEvent = new AsyncManualResetEvent();
        readonly AsyncLock             tradeDataLocker     = new AsyncLock();
        readonly Timer                 ordersUpdateTimer;
        readonly Timer                 balancesRefreshingTimer;

        StreamWriter positionsStreamWriter;
        bool         run;
        bool         isStopRequested;

        public BookFill(ITradeModelContext           tradeModelContext,
                        IEnumerable<ITradeConnector> tradeConnectors,
                        IEnumerable<IHedgeConnector> hedgeConnectors,
                        ICancellationTokenProvider   tokenProvider,
                        IIdGenerator                 requestIdGenerator,
                        IIdGenerator                 orderIdGenerator,
                        ILogger                      logger,
                        IUdpSender                   udpSender,
                        IUdpReceiver                 udpReceiver)
        {
            context = (BookFillContext)tradeModelContext;

            childSource             = CancellationTokenSource.CreateLinkedTokenSource(tokenProvider.Token);
            this.requestIdGenerator = requestIdGenerator;
            this.orderIdGenerator   = orderIdGenerator;
            this.logger             = logger;
            this.udpSender          = udpSender;
            this.udpReceiver        = udpReceiver;

            InitDataConnectors();
            Dictionary<string, AccountState> accountStatesByPubKey = InitTradeAndHedgeConnectors(tradeConnectors, hedgeConnectors);
            Dictionary<string, decimal>      posByIsin             = OpenPositionFile();
            CreateVariablesStates();
            CreateMMStates(accountStatesByPubKey, posByIsin);
            SaveAllPositions();
            SubscribeToEvents();

            ordersUpdateTimer         =  new Timer(context.MinOrdersNextActionDelayMuMs);
            ordersUpdateTimer.Elapsed += OrdersUpdateTimer_Elapsed;

            balancesRefreshingTimer         =  new Timer(BalancesRefreshingTimeoutMs);
            balancesRefreshingTimer.Elapsed += BalancesRefreshingTimer_Elapsed;

            variableMessageProcessor = new ActionBlock<VariableData>((Action<VariableData>)ProcessVariableMessage,
                                                                     new ExecutionDataflowBlockOptions
                                                                     {
                                                                         MaxDegreeOfParallelism = 1, CancellationToken = childSource.Token
                                                                     });

            tradeHedger = new ActionBlock<OrderMessage>((Action<OrderMessage>)ProcessHedgeExecution,
                                                        new ExecutionDataflowBlockOptions
                                                        {
                                                            MaxDegreeOfParallelism = 1, CancellationToken = childSource.Token, SingleProducerConstrained = true
                                                        });

            udpMessageProcessor = new ActionBlock<string>((Action<string>)ProcessUdpMessage,
                                                          new ExecutionDataflowBlockOptions
                                                          {
                                                              MaxDegreeOfParallelism    = 1,
                                                              CancellationToken         = childSource.Token,
                                                              SingleProducerConstrained = true
                                                          });

            udpReceiver.Initialize(context.UDPListenPort, udpMessageProcessor, childSource.Token);
            udpSender.Initialize(context.UDPSendPort);
        }

        public async Task Start()
        {
            foreach (AccountState state in accountStates.Values) state.Connector.Start();
            foreach (DataConnectorContext dataConnectorContext in context.DataConnectorContexts) dataConnectorContext.Connector.Start();

            try { await connectedResetEvent.WaitAsync(childSource.NewPairedTimeoutToken(WaitingTimeoutMs)); }
            catch (TaskCanceledException)
            {
                string errorMessage = $"Not all connectors could connect in assigned timeout of {WaitingTimeoutMs}ms.";
                logger.Enqueue(errorMessage);
                Stop(true, errorMessage);
                return;
            }

            connectedResetEvent.Reset();

            GetActiveOrders(false);
            CancelAllOrders();
            await Task.Delay(WaitingTimeoutMs);
            await GetPosAndMoney();
            logger.Enqueue("Got PosAndMoney. Setting run=true.");
            run = true;

            ordersUpdateTimer?.Start();
            balancesRefreshingTimer?.Start();

            var tasksToWait = new List<Task> {variableMessageProcessor.Completion};
            if (context.UseUdp) tasksToWait.Add(udpReceiver.Receive());

            await await Task.WhenAny(tasksToWait);

            logger.Enqueue("WhenAny released control because some task finished. Going to wait for exitStartResetEvent.");
            await exitStartResetEvent.WaitAsync();

            logger.Enqueue("Going to exit Start.");
        }

        public void Stop(bool isEmergencyStop, string errorDescription = null)
        {
            logger.Enqueue($"Stop was called with error description={errorDescription}");

            if (isStopRequested)
            {
                logger.Enqueue("Stop was already called. Exit stop.");
                return;
            }

            isStopRequested = true;

            logger.Enqueue("Going to set run=false.");
            run = false;

            if (context.UseUdp && isEmergencyStop)
            {
                if (!string.IsNullOrEmpty(errorDescription))
                {
                    string errorMessage = $"{context.InstanceName};\nERROR;\n{errorDescription}";
                    logger.Enqueue($"Going to send error message: {errorMessage}.");
                    udpSender?.SendMessage(errorMessage);
                }

                logger.Enqueue("Going to send STOP");
                udpSender?.SendMessage($"{context.InstanceName};STOP");
            }

            variableMessageProcessor.Complete();
            tradeHedger.Complete();
            udpMessageProcessor.Complete();

            ordersUpdateTimer?.Stop();

            logger.Enqueue("Going to get active orders.");
            GetActiveOrders(true);

            logger.Enqueue("Going to cancel all orders.");
            CancelAllOrders();

            logger.Enqueue("Going to stop all connectors.");
            StopAllConnectors();

            logger.Enqueue("Going cancel child source.");
            childSource.Cancel();

            logger.Enqueue($"Going to wait {WaitingTimeoutMs}ms.");
            Task.Delay(WaitingTimeoutMs).Wait();

            logger.Enqueue("Going to set exitStartResetEvent.");
            if (!exitStartResetEvent.IsSet) exitStartResetEvent.Set();
        }

        void BalancesRefreshingTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var timer = (Timer)sender;
            timer.Stop();

            try
            {
                GetPosAndMoney().Wait(childSource.NewPairedTimeoutToken(WaitingTimeoutMs));
                logger.Enqueue("Got all balances.\n");
            }
            finally { timer.Start(); }
        }

        void OrdersUpdateTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            ordersUpdateTimer.Stop();

            if (!run) return;

            DateTime timestamp = DateTime.UtcNow;
            foreach (KeyValuePair<string, int> pair in context.NextActionDelayByVariable)
            {
                string variableName    = pair.Key;
                int    nextActionDelay = pair.Value;

                VariableData variable = variables[variableName];

                double timeout = (timestamp - variable.LastUpdatedTimestamp).TotalMilliseconds;
                if (timeout < nextActionDelay) continue;

                logger.Enqueue($"Timeout={timeout}ms has passed for {variableName}. Going to check if we should already stop.");

                //иногда нужно выставлять, даже если цены не меняются. но делаем это не дольше, чем StopOnStuckBookTimeoutSec
                bool arePricesNew = BookHelpers.ArePricesNew(variable.PricesState.Book, true);
                if (TradeModelHelpers.IsStuckLongToStop(variable.PricesState, arePricesNew, variableName, logger, context.StopOnStuckBookTimeoutSec))
                {
                    Stop(true,
                         $"{variableName} book is stuck with bid={variable.PricesState.Book.BestBid} ask={variable.PricesState.Book.BestAsk} " +
                         $"for at least {context.StopOnStuckBookTimeoutSec} seconds.");
                    return;
                }

                variable.LastUpdatedTimestamp = timestamp;

                if (!run) return;

                //variableMessages.Enqueue(variable);
                logger.Enqueue($"Going to enqueue variable: {variable.PricesState} because of {nextActionDelay}ms timeout.");
                variableMessageProcessor.Post(variable);
            }

            ordersUpdateTimer.Start();
        }

        void ProcessVariableMessage(VariableData variableData)
        {
            try
            {
                using (tradeDataLocker.Lock(childSource.NewPairedTimeoutToken(WaitingTimeoutMs)))
                {
                    if (childSource.IsCancellationRequested || !run) return;
                    ProcessPricesMessage(variableData);
                }
            }
            catch (OperationCanceledException) { logger.Enqueue("Error: ProcessPricesMessage async lock was canceled upon timeout."); }
            catch (DivideByZeroException ex) { logger.Enqueue(ex.MakeString()); }
        }

        void ProcessPricesMessage(VariableData variable)
        {
            var changedTradeIsins = new HashSet<string>();

            if (!CheckPricesReady()) return;

            if (!SetFormulaPrices(variable.BaseFormulas, changedTradeIsins) || !SetFormulaPrices(variable.PredictorFormulas, changedTradeIsins)) return;

            foreach (string changedTradeIsin in changedTradeIsins)
            {
                IsinMMState        mmState    = mmStates[changedTradeIsin];
                BookFillIsinParams isinParams = context.IsinsToTrade[changedTradeIsin];

                if (!mmState.FormulasValuesSet)
                {
                    logger.Enqueue($"These formulas: {mmState.NotSetFormulas} values were not set yet for isin {changedTradeIsin}.");
                    continue;
                }

                if (mmState.PredictorsCoefsSet)
                {
                    mmState.ShiftPredictorsCoefs(isinParams.PredictorToBaseApproachingSpeed, context.CheckPricesMatch, out bool doPricesMatch);
                    if (context.CheckPricesMatch && !doPricesMatch)
                    {
                        logger.Enqueue($"ShiftPredictorsCoefs Error. {changedTradeIsin} predictors did not match basePrice. {variable.PricesState}");
                        continue;
                    }
                }
                else mmState.SetInitialPredictorsCoefs();

                (decimal minPredictorsBid, decimal maxPredictorsAsk, decimal predictorBidOriginal, decimal predictorAskOriginal, decimal dealShift) =
                    mmState.GetPredictorsBidAsk();

                if (!run) return;

                mmState.TryGetMarginPosition(changedTradeIsin, out decimal marginPosition);

                if (isinParams.MarketMakingModel == MarketMakingModels.Obligations)
                {
                    var usedPriceData = new UsedAddOrderPriceData(predictorBidOriginal, predictorAskOriginal, dealShift, isinParams.MinStep);
                    ProcessObligations(mmState, isinParams, changedTradeIsin, minPredictorsBid, maxPredictorsAsk, usedPriceData, marginPosition);
                }
                else ProcessRandomFill(mmState, isinParams, changedTradeIsin, minPredictorsBid, marginPosition, maxPredictorsAsk);

                //если интервал прошёл, выбираем новый
                if (!mmState.DelayToNextOrdersActionPassed) return;
                mmState.SetDelayToNextOrdersAction(isinParams.OrdersNextActionDelayMuMs, isinParams.OrdersNextActionDelayMuMs * OrdersNextActionDelaySigmaFrac);
                mmState.RestartMeasuringDelayToNextAction();
            }
        }

        bool SetFormulaPrices(List<FormulaContext> formulas, HashSet<string> changedTradeIsinsP)
        {
            foreach (FormulaContext formula in formulas)
            {
                try
                {
                    //костыль для случаев, когда только один источник данных и для центра и для предикторов. проверки на случай, что формулы не отличаются от цен.
                    formula.SetPrices(context.CheckPricesMatch, out bool doPricesMatchFormula);
                    if (context.CheckPricesMatch && !doPricesMatchFormula)
                    {
                        logger.Enqueue("SetPrices Error. Assuming that we have single source base price and single predictor. " +
                                       $"Formulas do not match market prices. Formula: {formula.ExpressionString} with variables: {formula.VariablesValuesString}.");
                        return false;
                    }
                }
                catch (Exception e)
                {
                    logger.Enqueue($"Exception: {e.Message} while trying to calculate formula: {formula.ExpressionString} " +
                                   $"with variables: {formula.VariablesValuesString}.");
                    return false;
                }

                changedTradeIsinsP.Add(formula.TradeIsin);
            }

            return true;
        }

        bool CheckPricesReady()
        {
            if (!variables.Values.All(data => data.PricesState.ArePricesReady) || !utilityPricesStates.Values.All(state => state.ArePricesReady))
            {
                List<KeyValuePair<string, VariableData>> notReadyVariables = variables.Where(pair => !pair.Value.PricesState.ArePricesReady).ToList();
                foreach (KeyValuePair<string, VariableData> pair in notReadyVariables)
                {
                    foreach (FormulaContext baseFormula in pair.Value.BaseFormulas) baseFormula.ValuesSet                = false;
                    foreach (FormulaContext predictorFormula in pair.Value.PredictorFormulas) predictorFormula.ValuesSet = false;
                }

                IEnumerable<KeyValuePair<string, PricesState>> notReadyStates = notReadyVariables.
                                                                                Select(pair => new KeyValuePair<string, PricesState>(pair.Key,
                                                                                           pair.Value.PricesState)).
                                                                                Union(utilityPricesStates.Where(pair => !pair.Value.ArePricesReady));

                logger.Enqueue("Some prices are not ready: " +
                               $"{string.Join('|', notReadyStates.Select(pair => $"{pair.Key} bid={pair.Value.Book.BestBid} ask={pair.Value.Book.BestAsk}"))}. " +
                               "Trying to cancel orders.");
                foreach (IsinMMState mmState in mmStates.Values) mmState.PredictorsCoefsSet = false;
                CancelAllOrders();
                return false;
            }

            return true;
        }

        bool IsEnoughBalance(IsinMMState        mmState,
                             BookFillIsinParams isinParams,
                             string             isin,
                             OrderSide          side,
                             decimal            orderQty,
                             decimal            orderPrice,
                             string             callMethod,
                             decimal            marginPosition)
        {
            string buyCurrency  = isinParams.BuyMarginCurrency;
            string sellCurrency = isinParams.SellMarginCurrency;

            if (!mmState.TradeAccountState.AvailableSpotBalances.TryGetValue(buyCurrency, out decimal buyBalance))
            {
                logger.Enqueue($"{callMethod}. {buyCurrency} was not found in balances dictionary. " + $"Skip this order adding for isin {isin}.");
                return false;
            }

            if (!mmState.TradeAccountState.AvailableSpotBalances.TryGetValue(sellCurrency, out decimal sellBalance))
            {
                logger.Enqueue($"{callMethod}. {sellCurrency} was not found in balances dictionary. " + $"Skip this order adding for isin {isin}.");
                return false;
            }

            //для margin buyBalance = sellBalance
            decimal convertToFiatMid = (mmState.ConversionToFiatPricesState.Book.BestBid + mmState.ConversionToFiatPricesState.Book.BestAsk) / 2;

            decimal checkBalanceQty;

            //если это маржинальный рынок, и выставляем заявку противоположно позиции
            if (context.IsMarginMarket && (marginPosition > 0 && side == OrderSide.Sell || marginPosition < 0 && side == OrderSide.Buy))

                //если уменьшаем позицию, то считаем маржу на количество 0 (должно получиться 0), потому что не должно браться залога за закрытие.
                //если переворачиваемся, то считаем маржу на кусок, который будет открыт после переворота.
                checkBalanceQty  = Math.Abs(marginPosition) > orderQty ? 0 : orderQty - Math.Abs(marginPosition);
            else checkBalanceQty = orderQty;

            decimal balance = side == OrderSide.Buy ? buyBalance : sellBalance;
            bool isBalanceEnough = TradeModelHelpers.IsEnoughBalance(context.IsMarginMarket,
                                                                     isinParams.IsReverse.Value,
                                                                     orderPrice,
                                                                     checkBalanceQty,
                                                                     convertToFiatMid,
                                                                     isinParams.LotSize,
                                                                     isinParams.Leverage,
                                                                     side,
                                                                     balance);

            if (!isBalanceEnough)
            {
                logger.Enqueue($"{callMethod}. Not enough balance to add order. "                                                                         +
                               $"Chose price={orderPrice};qty={orderQty};checkBalanceQty={checkBalanceQty};side={side};marginPosition={marginPosition}. " +
                               $"Available balances:{buyCurrency}={buyBalance};{sellCurrency}={sellBalance}. "                                            +
                               $"Skip this order adding for isin {isin}.");

                return false;
            }

            return true;
        }

        string CreateClientOrderId(string isin, OrderSide side)
        {
            //string timestamp = DateTime.UtcNow.ToString("MMdd-HHmmss.fff");
            //string buyClientOrderId = $"{state.Isin}_{timestamp}_B_{orderIdGenerator.Id}";
            //string sellClientOrderId = $"{state.Isin}_{timestamp}_S_{orderIdGenerator.Id}";
            int    orderId       = orderIdGenerator.Id;
            string timestamp     = DateTime.UtcNow.ToString("MMdd-HHmmss.fff");
            string sideStr       = side == OrderSide.Buy ? "B" : "S";
            string clientOrderId = $"{isin}_{timestamp}{sideStr}{orderId}";
            return clientOrderId;
        }

        async Task GetPosAndMoney()
        {
            foreach (AccountState state in accountStates.Values)
            {
                ITradeConnector tradeConnector = state.Connector;
                tradeConnector.GetPosAndMoney(requestIdGenerator.Id);

                await state.WaitForBalance(WaitingTimeoutMs, state.Connector.Name);
            }
        }

        void GetActiveOrders(bool beforeExit)
        {
            logger.Enqueue($"Got these connectors: {string.Join(';', accountStates.Values.Select(state => $"{state.Connector.ExchangeName}_{state.Connector.Name}"))}");
            foreach (AccountState state in accountStates.Values)
            {
                ITradeConnector tradeConnector = state.Connector;

                logger.Enqueue($"Going to get active orders for {tradeConnector.ExchangeName}_{tradeConnector.Name}");
                tradeConnector.GetActiveOrders(requestIdGenerator.Id);

                logger.Enqueue($"Going to wait for active orders for {tradeConnector.ExchangeName}_{tradeConnector.Name}");
                state.WaitForActiveOrders(WaitingTimeoutMs, state.Connector.Name, beforeExit);
            }
        }

        void CancelAllOrders()
        {
            foreach (KeyValuePair<string, IsinMMState> pair in mmStates)
            {
                string             isin         = pair.Key;
                IsinMMState        state        = pair.Value;
                List<OrderMessage> activeOrders = state.GetAllActiveOrders();
                CancelOrders(activeOrders, state, isin);
            }
        }

        void CancelOrders(List<OrderMessage> activeOrders, IsinMMState state, string isin)
        {
            logger.Enqueue(activeOrders.Count > 0
                               ? $"Going to send cancel for orders:\n{string.Join('\n', activeOrders)}"
                               : $"No orders to cancel for isin {isin}");
            foreach (OrderMessage order in activeOrders) CancelSingleOrder(isin, order.Side, order.OrderId, state, false, order.ToString());
        }

        void CancelSingleOrder(string isin, OrderSide side, string orderId, IsinMMState state, bool logSingleCancel, string orderStr)
        {
            if (logSingleCancel) logger.Enqueue($"Going to send cancel for order:\n{orderStr} for isin {isin}");

            //нужно обновить до отправки заявки, потому что отправка заявки может быть синхронная, через rest. и OrderCanceled вызовется до TryUpdateLocalOrderState.
            state.TryUpdateLocalOrderState(orderId, LocalOrderStatus.CancelPending, side);
            state.TradeConnector.CancelOrder(orderId, orderIdGenerator.Id);
        }

        void SaveAllPositions()
        {
            positionsStreamWriter.BaseStream.SetLength(0);

            //logger.Enqueue("set position 0");
            positionsStreamWriter.
                Write($"{string.Join('\n', mmStates.Select(pair => $"{pair.Key};{pair.Value.PositionFiat.ToString(CultureInfo.InvariantCulture)}"))}");

            //logger.Enqueue("dumped");
        }

        void ProcessHedgeExecution(OrderMessage execution)
        {
            HedgeTrade(execution.Isin, execution.Side, execution.TradeQty);
        }

        void ProcessUdpMessage(string message)
        {
            logger.Enqueue($"Received UDP message: {message}.");
            string[] tokens = message.Split(';');

            if (tokens.Length < 4)
            {
                logger.Enqueue("Message has to have 4 tokens at least.");
                return;
            }

            string action  = tokens[0];
            string isin    = tokens[1];
            string sideStr = tokens[2];

            //HedgeTrade ждёт сообщение о сделке, а мы шлём сторону, которую хотим видеть уже в хедже. поэтому инвертируем сторону.
            OrderSide side = sideStr == "Buy" ? OrderSide.Sell : OrderSide.Buy;
            if (!decimal.TryParse(tokens[3], NumberStyles.Float, CultureInfo.InvariantCulture, out decimal qty))
            {
                logger.Enqueue($"Couldn't parse {tokens[3]} as decimal number.");
                return;
            }

            if (action != "Hedge")
            {
                logger.Enqueue("Only Hedge action can be processed at the moment.");
                return;
            }

            HedgeTrade(isin, side, qty);
        }

        void HedgeTrade(string isin, OrderSide side, decimal tradeQty)
        {
            if (!mmStates.TryGetValue(isin, out IsinMMState state)) return;
            if (!context.IsinsToTrade.TryGetValue(isin, out BookFillIsinParams isinParams)) return;

            string    hedgeIsin = isinParams.Hedge.HedgeWithIsin;
            OrderSide hedgeSide = side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;

            decimal hedgeQty = tradeQty * isinParams.Hedge.TradeToHedgeCoef;
            hedgeQty = Math.Round(hedgeQty / isinParams.Hedge.HedgeMinQty) * isinParams.Hedge.HedgeMinQty;

            UnlimitedOrderBook<long> hedgeBook = state.HedgePricesState.Book;

            decimal hedgeMinstep = isinParams.Hedge.HedgeMinStep;
            decimal slippageFrac = isinParams.Hedge.HedgeSlippagePricePerc / 100;
            decimal hedgeBid     = hedgeBook.BestBid;
            decimal hedgeAsk     = hedgeBook.BestAsk;

            decimal hedgePrice;
            decimal priceShift;

            if (hedgeSide == OrderSide.Buy)
            {
                hedgePrice = hedgeAsk * (1 + slippageFrac);
                priceShift = hedgeAsk * slippageFrac;
            }
            else
            {
                hedgePrice = hedgeBid * (1 - slippageFrac);
                priceShift = -1       * hedgeBid * slippageFrac;
            }

            hedgePrice = Math.Round(hedgePrice / hedgeMinstep) * hedgeMinstep;

            string hedgeClientOrderId = CreateClientOrderId(hedgeIsin, hedgeSide);

            if (!run) return;

            logger.Enqueue($"Going to hedge {isin};{side};{tradeQty}. " +
                           $"Going to add {hedgeIsin}|{hedgeSide}|price={hedgePrice}|qty={hedgeQty}|{hedgeClientOrderId}.");

            if (context.UseUdp) usedPricesDatas.TryAdd(hedgeClientOrderId, new UsedAddOrderPriceData(hedgeBid, hedgeAsk, priceShift, hedgeMinstep));

            var hedgeConnector = (IHedgeConnector)state.HedgeAccountState.Connector;

            hedgeConnector.AddHedgeOrder(hedgeClientOrderId, hedgeIsin, hedgeSide, hedgePrice, hedgeQty, slippageFrac, requestIdGenerator.Id);
        }

        void StopAllConnectors()
        {
            foreach (DataConnectorContext dataConnectorContext in context.DataConnectorContexts)
            {
                logger.Enqueue($"Going to stop data connector {dataConnectorContext.Connector.ExchangeName}.");
                dataConnectorContext.Connector.Stop();
            }

            foreach (AccountState state in accountStates.Values)
            {
                logger.Enqueue($"Going to stop connector {state.Connector.ExchangeName}.");
                state.Connector.Stop();
            }
        }
    }
}
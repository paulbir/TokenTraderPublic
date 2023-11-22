using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Nito.AsyncEx;
using SharedDataStructures;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SharedTools;
using SharedTools.Interfaces;
using TokenTrader.Initialization;
using TokenTrader.Interfaces;
using TokenTrader.OrderBook;
using TokenTrader.State;
using Timer = System.Timers.Timer;

namespace TokenTrader.TradeModels
{
    class SimultaneousTrades : ITradeModel
    {
        const int    AbsoluteMinDelayMs                      = 100;
        const int    MinTargetDelayMs                        = 1000;
        const double AverageTradingSpeedProbability          = 0.85;
        const double AverageTradingSpeedDelaySigmaFrac       = 0.4;
        const double FastTradingSpeedContinuationProbability = 0.85;
        const int    LastTradingSpeedsWindowSize             = 5;
        const double ChoosePreviousBasePriceProbability      = 0.7;
        const double HighVolumeProbability                   = 0.015;
        const double PriceBestOffsetSigmaFrac                = 0.3;
        const double RoundTradeQtyProbability                = 0.2;
        const int    MinSafeBuyModeRangeFromBidMinsteps      = 10;

        const int DataTimeoutSeconds               = 180;
        const int WaitingTimeoutMs                 = 10000;
        const int DataRefreshingTimeoutMs          = 15000;
        const int ReconnectOnCrossedBookTimeoutSec = 300;

        const int BookErrorQueueWindowMs       = 10000;
        const int NumBookErrorsInWindowToThrow = 5;

        readonly SimultaneousTradesContext                            context;
        readonly List<ITradeConnector>                                connectors;
        readonly CancellationTokenSource                              childSource;
        readonly IIdGenerator                                         requestIdGenerator;
        readonly IIdGenerator                                         orderIdGenerator;
        readonly ILogger                                              logger;
        readonly ConcurrentDictionary<string, IsinVolumeTradingState> isinTradingStates          = new ConcurrentDictionary<string, IsinVolumeTradingState>();
        readonly ConcurrentDictionary<string, PricesState>            pricesStates               = new ConcurrentDictionary<string, PricesState>();
        readonly ConcurrentDictionary<string, AccountState>           accountStates              = new ConcurrentDictionary<string, AccountState>();
        readonly ConcurrentDictionary<string, bool>                   areDataConnectorsConnected = new ConcurrentDictionary<string, bool>();
        readonly HashSet<string>                                      usedCurrencies             = new HashSet<string>();
        readonly ConcurrentDictionary<string, List<string>>           isinsByConnector           = new ConcurrentDictionary<string, List<string>>();

        readonly AsyncManualResetEvent connectedResetEvent = new AsyncManualResetEvent();
        readonly AsyncLock             tradeDataLocker     = new AsyncLock();

        readonly Timer dataRefreshingTimer;

        int  numSentTrades;
        int  prevNumSentTrades;
        bool gotBalances;
        bool run;

        public SimultaneousTrades(ITradeModelContext           tradeModelContext,
                                  IEnumerable<ITradeConnector> connectors,

                                  //IDataConnector btcPriceConnector,
                                  ICancellationTokenProvider tokenProvider,
                                  IIdGenerator               requestIdGenerator,
                                  IIdGenerator               orderIdGenerator,
                                  ILogger                    logger)
        {
            context = (SimultaneousTradesContext)tradeModelContext;
            if (context.TradeConnectorsSettings.Count < 2)
                throw new ConfigErrorsException("SimultaneousTrades model needs at least 2 connector instances, " +
                                                "so there have to be at least 2 connectors settings.");

            this.connectors = connectors.ToList();

            //this.btcPriceConnector = btcPriceConnector;
            childSource             = CancellationTokenSource.CreateLinkedTokenSource(tokenProvider.Token);
            this.requestIdGenerator = requestIdGenerator;
            this.orderIdGenerator   = orderIdGenerator;
            this.logger             = logger;

            dataRefreshingTimer         =  new Timer(DataRefreshingTimeoutMs);
            dataRefreshingTimer.Elapsed += DataRefreshingTimer_Elapsed;

            Init();
        }

        public async Task Start()
        {
            //btcPriceConnector.Start();
            //accountStates["data_trade"].Connector.Start();

            foreach (AccountState state in accountStates.Values) state.Connector.Start();
            foreach (DataConnectorContext dataConnectorContext in context.DataConnectorContexts) dataConnectorContext.Connector.Start();

            //Console.WriteLine($"starting to wait for event {Thread.CurrentThread.ManagedThreadId}");
            try { await connectedResetEvent.WaitAsync(childSource.NewPairedTimeoutToken(WaitingTimeoutMs)); }
            catch (TaskCanceledException)
            {
                logger.Enqueue($"Not all connectors could connect in assigned timeout of {WaitingTimeoutMs}ms.");
                return;
            }

            //Console.WriteLine($"finished waiting for event {Thread.CurrentThread.ManagedThreadId}");
            connectedResetEvent.Reset();

            //Console.WriteLine($"reset event {Thread.CurrentThread.ManagedThreadId}");

            await CancelAllOrders();

            //Console.WriteLine("got active orders");

            await GetPosAndMoney();
            gotBalances = true;

            //Console.WriteLine("got balances");
            run = true;
            var tasks = new List<Task>();
            foreach (string isin in context.IsinsToTrade.Keys)
                tasks.Add(Task.Factory.StartNew(async () => await RunTradingLoop(isin), TaskCreationOptions.LongRunning).Unwrap());

            dataRefreshingTimer.Start();

            Task completedTask = await Task.WhenAny(tasks);
            await completedTask;

            logger.Enqueue($"{completedTask.Status} {completedTask.IsFaulted} tasks finished");
        }

        public void Stop(bool isEmergencyStop, string unhandledExceptionMessage = null)
        {
            run = false;
            if (numSentTrades > 0) CancelAllOrders(true).Wait(WaitingTimeoutMs);
            foreach (DataConnectorContext dataConnectorContext in context.DataConnectorContexts) dataConnectorContext.Connector.Stop();
            foreach (AccountState connectorState in accountStates.Values) connectorState.Connector?.Stop();
        }

        void DataRefreshingTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            //если нету сделок и балансы правильные, выходим
            if (numSentTrades == prevNumSentTrades && gotBalances) return;

            var timer = (Timer)sender;
            timer.Stop();

            try
            {
                using (tradeDataLocker.Lock(childSource.NewPairedTimeoutToken(WaitingTimeoutMs)))
                {
                    CancelAllOrders().Wait(childSource.NewPairedTimeoutToken(WaitingTimeoutMs));
                    logger.Enqueue("Got all active orders and sent cancel requests if got any.");
                    GetPosAndMoney().Wait(childSource.NewPairedTimeoutToken(WaitingTimeoutMs));
                    logger.Enqueue("Got all balances.\n");
                    gotBalances = true;
                }
            }
            catch (OperationCanceledException) { logger.Enqueue("Error: DataRefreshingTimer_Elapsed async lock was canceled upon timeout."); }
            finally
            {
                timer.Start();
                prevNumSentTrades = numSentTrades;
            }
        }

        void Init()
        {
            Directory.CreateDirectory("turnovers");
            foreach (KeyValuePair<string, SimultaneousTradesIsinParams> pair in context.IsinsToTrade)
            {
                string                       isin       = pair.Key;
                SimultaneousTradesIsinParams isinParams = pair.Value;

                SortedList<DateTime, decimal> turnoverPoints = ReadTurnoverPlan(isin);
                string turnoversPath = Path.Combine("turnovers", $"{isin.Replace("/", "[slash]")}_{DateTime.UtcNow.Date:dd-MM-yyyy}.csv");
                var turnoverWriter = new StreamWriter(turnoversPath, append: true);

                string actualFullIsin = $"{connectors[0].ExchangeName}_{isin}";
                bool   checkBookCross = !context.NoBookCrossCheckVariables.Contains(actualFullIsin);
                var    pricesState    = new PricesState(context.MaxSpreadForReadyPricesPerc, 0, BookErrorQueueWindowMs, checkBookCross);
                pricesStates.TryAdd(actualFullIsin, pricesState);

                (string isin, string exchange) conversionPair           = context.ConversionToFiatIsinByTradeIsin[isin];
                string                         conversionActualFullIsin = $"{conversionPair.exchange}_{conversionPair.isin}";
                if (!pricesStates.TryGetValue(conversionActualFullIsin, out PricesState conversionToFiatPricesState))
                {
                    bool conversionCheckBookCross = !context.NoBookCrossCheckVariables.Contains(conversionActualFullIsin);
                    conversionToFiatPricesState = new PricesState(context.MaxSpreadForReadyPricesPerc, 0, BookErrorQueueWindowMs, checkBookCross);
                    pricesStates.TryAdd($"{conversionPair.exchange}_{conversionPair.isin}", conversionToFiatPricesState);
                }

                isinTradingStates.TryAdd(isin,
                                         new IsinVolumeTradingState(isin,
                                                                    LastTradingSpeedsWindowSize,
                                                                    logger,
                                                                    childSource,
                                                                    turnoverPoints,
                                                                    turnoverWriter,
                                                                    pricesState,
                                                                    conversionToFiatPricesState));

                //этот хэшсэт будет фильтровать получаемые лимиты
                usedCurrencies.Add(isinParams.BuyMarginCurrency);
                usedCurrencies.Add(isinParams.SellMarginCurrency);
            }

            InitConnectors();
            SubscribeToEvents();
        }

        void InitConnectors()
        {
            if (context.TradeConnectorsSettings == null)
                throw new ConfigErrorsException("ConnectorsSettings couldn't be bind to Settings.ConnectorsSettings property.");
            if (context.TradeConnectorsSettings.Count < 2)
                throw new ConfigErrorsException("ConnectorsSettings array has to contain at least 2 elements for SimultaneousTrades.");
            if (context.TradeConnectorsSettings.Count != connectors.Count)
                throw new ConfigErrorsException("ConnectorsSettings array has to contain the same number of elements as " +
                                                "Connectors list for SimultaneousTrades.");

            //объединяем торговые исины и, если так получилось, что из торгового коннектора нужно получать исины для конвертации в фиат,
            // то на них подписываемся тоже
            List<string> isinsToGet = context.IsinsToTrade.Keys.Union(context.AdditionalIsinsToGetFromTradeConnector).ToList();
            for (int i = 0; i < context.TradeConnectorsSettings.Count; i++)
            {
                ConnectorSettings connectorSettings = context.TradeConnectorsSettings[i];
                ITradeConnector   connector         = connectors[i];
                string            connectorName     = i == 0 ? "data_trade" : $"trade_only_{i}";

                //только первый коннектор будет получать данные. остальные только торгуют.
                connector.Init(i == 0 ? isinsToGet : null, i == 0 ? DataTimeoutSeconds : -1, connectorSettings.PubKey, connectorSettings.SecKey, connectorName);
                accountStates.TryAdd(connectorName, new AccountState(connector, logger, childSource));
                isinsByConnector.TryAdd(connectorName, isinsToGet);
            }

            for (int i = 0; i < context.DataConnectorContexts.Count; i++)
            {
                DataConnectorContext dataConnectorContext = context.DataConnectorContexts[i];
                IDataConnector       connector            = dataConnectorContext.Connector;
                var                  dataIsinsToGet       = dataConnectorContext.IsinsToGet.ToList();
                string               connectorName        = $"data_{connector.ExchangeName}_{i}";
                connector.Init(dataIsinsToGet, DataTimeoutSeconds, "", "", connectorName);
                areDataConnectorsConnected[connector.Name] = false;
                isinsByConnector.TryAdd(connectorName, dataIsinsToGet);
            }
        }

        void SubscribeToEvents()
        {
            foreach (AccountState connectorState in accountStates.Values)
            {
                var connector = connectorState.Connector;
                connector.Connected               += Connector_Connected;
                connector.Disconnected            += Connector_Disconnected;
                connector.ActiveOrdersListArrived += Connector_ActiveOrdersListArrived;
                connector.NewOrderAdded           += Connector_NewOrderAdded;
                connector.OrderCanceled           += Connector_OrderCanceled;
                connector.OrderReplaced           += Connector_OrderReplaced;
                connector.ExecutionReportArrived  += Connector_ExecutionReportArrived;
                connector.BalanceArrived          += Connector_BalanceArrived;
                connector.PositionArrived         += Connector_PositionArrived;
                connector.ErrorOccured            += Connector_ErrorOccured;
            }

            var dataTradeConnector = accountStates["data_trade"].Connector;
            dataTradeConnector.BookSnapshotArrived += Connector_BookSnapshotArrived;
            dataTradeConnector.BookUpdateArrived   += Connector_BookUpdateArrived;
            dataTradeConnector.TickerArrived       += Connector_TickerArrived;

            foreach (DataConnectorContext dataConnectorContext in context.DataConnectorContexts)
            {
                IDataConnector connector = dataConnectorContext.Connector;
                connector.Connected           += Connector_Connected;
                connector.Disconnected        += Connector_Disconnected;
                connector.BookSnapshotArrived += Connector_BookSnapshotArrived;
                connector.BookUpdateArrived   += Connector_BookUpdateArrived;
                connector.TickerArrived       += Connector_TickerArrived;
            }

            //btcPriceConnector.Connected += Connector_Connected;
            //btcPriceConnector.Disconnected += Connector_Disconnected;
            //btcPriceConnector.TickerArrived += Connector_TickerArrived;
        }

        static SortedList<DateTime, decimal> ReadTurnoverPlan(string isin)
        {
            bool     foundPlan      = false;
            bool     gotHeader      = false;
            string[] headerTokens   = null;
            var      turnoverPoints = new SortedList<DateTime, decimal>();

            string plansPath = Path.Combine("plans", $"{isin.Replace("/", "[slash]")}_turnover_plan");
            foreach (string line in File.ReadLines(plansPath))
            {
                string[] tokens = line.Split(';');
                if (!gotHeader)
                {
                    headerTokens = tokens;
                    gotHeader    = true;
                    continue;
                }

                DateTime planDate = DateTime.ParseExact(tokens[0], "dd/MM/yyyy", CultureInfo.InvariantCulture);

                if (planDate == DateTime.UtcNow.Date)
                {
                    if (foundPlan)
                        throw new
                            ConfigErrorsException($"Found more than one dates equal to UTC today {DateTime.UtcNow.Date} in turnover plan for isin {isin}.");
                    foundPlan = true;
                    for (int i = 1; i < tokens.Length; i++)
                    {
                        DateTime planTime  = DateTime.Parse(headerTokens[i]);
                        DateTime timePoint = planDate.AddHours(planTime.Hour).AddMinutes(planTime.Minute).AddSeconds(planTime.Second);
                        turnoverPoints.Add(timePoint, tokens[i].ToDecimal());
                    }
                }
            }

            if (!foundPlan) throw new ConfigErrorsException($"Couldn't find todays {DateTime.UtcNow.Date} turnover plan for isin {isin}.");

            return turnoverPoints;
        }

        async Task RunTradingLoop(string isin)
        {
            IsinVolumeTradingState state = isinTradingStates[isin];

            logger.Enqueue("\n");
            while (!childSource.IsCancellationRequested && run)
            {
                if (!TrySetTurnoverTargets(state))
                {
                    //если нет каких-то цен, то не крутимся в цикле с максимальной скоростью, а ждём 3 секунды перед
                    // следующей итерацией.
                    await Task.Delay(3000, childSource.Token);
                    continue;
                }

                TimeSpan delaySpan = ChooseNextDelayMs(state);
                if (delaySpan == TimeSpan.MaxValue) continue;

                //Console.WriteLine($"Going to wait for {delaySpan.TotalMilliseconds}ms. {Thread.CurrentThread.ManagedThreadId}");
                await Task.Delay(delaySpan, childSource.Token);

                //после паузы нельзя торговать. потому что иначе DoneTurnover > 0, и мы насчитаем при переходе на новый период,
                // что не доделали оборота.
                if (state.PauseType == PauseType.Pause) continue;

                if (childSource.IsCancellationRequested) return;

                try
                {
                    using (await tradeDataLocker.LockAsync(childSource.NewPairedTimeoutToken(WaitingTimeoutMs)))
                    {
                        state.ClearSentOrderIds();
                        if (!await TryExecuteTrade(state, delaySpan)) continue;

                        //Console.WriteLine($"Trade executed. {Thread.CurrentThread.ManagedThreadId}");

                        await Task.WhenAll(state.WaitForAllNewOrdersResponse(WaitingTimeoutMs), state.WaitForAllExecutionReports(WaitingTimeoutMs));
                        if (state.IsRoundtripMeasuringInProgess) state.StoreRoundtrip();
                        logger.Enqueue("\n");
                    }
                }
                catch (OperationCanceledException) { logger.Enqueue("Error: Trading loop async lock was canceled upon timeout."); }
            }
        }

        bool TrySetTurnoverTargets(IsinVolumeTradingState state)
        {
            string                       isin          = state.Isin;
            SimultaneousTradesIsinParams isinParams    = context.IsinsToTrade[isin];
            UnlimitedOrderBook<long>     book          = state.PricesState.Book;
            decimal                      bid           = context.UseVWAP ? book.GetOneSideVwap(OrderSide.Buy,  isinParams.VWAPQty) : book.BestBid;
            decimal                      ask           = context.UseVWAP ? book.GetOneSideVwap(OrderSide.Sell, isinParams.VWAPQty) : book.BestAsk;
            decimal                      conversionBid = state.ConversionToFiatPricesState.Book.BestBid;
            decimal                      conversionAsk = state.ConversionToFiatPricesState.Book.BestAsk;

            //нету цен пока. крутимся и ждём.
            if (bid == 0 && ask == 0 || bid >= ask || conversionBid == 0 && conversionAsk == 0 || conversionBid > conversionAsk)
            {
                logger.Enqueue($"Some of {(context.UseVWAP ? "VWAP" : "BEST")} prices are not ready or inconsistent for setting turnover targets: " +
                               $"bid={bid};ask={ask};conversionBid={conversionBid};conversionAsk={conversionAsk} for isin {isin}. Skip trade.");
                return false;
            }

            decimal mid           = (bid           + ask)           / 2;
            decimal conversionMid = (conversionBid + conversionAsk) / 2;

            DateTime now                       = DateTime.UtcNow;
            int      idxOfNearestGreaterPeriod = state.TargetFiatTurnoverPoints.IndexOfNearestGreaterKeyBinary(now);
            DateTime newTurnoverPeriod         = state.TargetFiatTurnoverPoints.Keys[idxOfNearestGreaterPeriod];

            //последний период и время перевалило, значит заканчиваем на сегодня.
            if (idxOfNearestGreaterPeriod >= state.TargetFiatTurnoverPoints.Count - 1 && now >= newTurnoverPeriod)
            {
                logger.Enqueue("Reached end of turnover plan for today. Will STOP trading. " +
                               $"Now={now}, Last plan point={state.TargetFiatTurnoverPoints.Keys.Last()} for isin {isin}\n");
                state.PauseType                 = PauseType.Stop;
                state.CurrentDoneTurnoverCrypto = 0;
                return true;
            }

            //период не сменился. ничего не делаем.
            if (newTurnoverPeriod == state.CurrentTurnoverPeriod) return true;

            TimeSpan spanToEndOfPeriod = newTurnoverPeriod - now;
            if (spanToEndOfPeriod.TotalHours >= 5)
            {
                throw new ConfigErrorsException($"Span from now to next period end is too large. Total hours = {spanToEndOfPeriod.TotalHours}. " +
                                                $"For isin {isin}.");
            }

            int msToTheEndOfPeriod = (int)Math.Round(spanToEndOfPeriod.TotalMilliseconds);

            //если промахнулись мимо оборота в этом периоде, то расбрасываем разницу на следующие периоды.
            if (state.CurrentDoneTurnoverCrypto > 0)
            {
                decimal turnoverDeviationCrypto = state.CurrentDoneTurnoverCrypto - state.CurrentTargetTurnoverCrypto;
                decimal turnoverDeviationFiat = TradeModelHelpers.FiatVolumeFromQty(context.IsMarginMarket,
                                                                                    isinParams.IsReverse.Value,
                                                                                    mid,
                                                                                    turnoverDeviationCrypto,
                                                                                    conversionMid,
                                                                                    isinParams.LotSize);
                int numUsedNextPeriodsToDistribute = state.DistributeTurnoverDeviationToNextPeriods(idxOfNearestGreaterPeriod, turnoverDeviationFiat);
                logger.Enqueue($"We missed with our turnover {state.CurrentDoneTurnoverCrypto} from target {state.CurrentTargetTurnoverCrypto}. " +
                               $"Distributed fiat difference {turnoverDeviationFiat} to {numUsedNextPeriodsToDistribute} next periods. "          +
                               $"For isin {isin}.\n");
            }

            decimal newTurnoverFiat = state.TargetFiatTurnoverPoints.Values[idxOfNearestGreaterPeriod];

            //в следующем периоду <= 0? значит пропускаем его и просто ждём.
            if (newTurnoverFiat <= 0)
            {
                logger.Enqueue($"New period taget turnover {newTurnoverFiat} <= 0. "                                    +
                               $"Will PAUSE trading for nearly {msToTheEndOfPeriod / 1000} seconds until next period. " +
                               $"Passed period={state.CurrentTurnoverPeriod}, Next period end={newTurnoverPeriod} for isin {isin}.\n");
                state.PauseType                 = PauseType.Pause;
                state.CurrentDoneTurnoverCrypto = 0;
                state.SpanToEndOfPeriod         = spanToEndOfPeriod;
                return true;
            }

            state.CurrentTargetTurnoverCrypto =
                TradeModelHelpers.QtyFromCurrencyFiatVolume(context.IsMarginMarket,
                                                            isinParams.IsReverse.Value,
                                                            newTurnoverFiat,
                                                            mid,
                                                            conversionMid,
                                                            isinParams.LotSize);
            state.CurrentTargetTurnoverCrypto = Math.Round(state.CurrentTargetTurnoverCrypto / isinParams.MinQty) * isinParams.MinQty;

            decimal minOrderVolumeCrypto = TradeModelHelpers.QtyFromCurrencyVolume(context.IsMarginMarket,
                                                                                   isinParams.IsReverse.Value,
                                                                                   isinParams.MinOrderVolume,
                                                                                   mid,
                                                                                   isinParams.LotSize);
            minOrderVolumeCrypto = Math.Round(minOrderVolumeCrypto / isinParams.MinQty) * isinParams.MinQty;

            decimal normalTradeQtyMuCrypto = TradeModelHelpers.QtyFromCurrencyVolume(context.IsMarginMarket,
                                                                                     isinParams.IsReverse.Value,
                                                                                     isinParams.MinTradeVolumeMu,
                                                                                     mid,
                                                                                     isinParams.LotSize);
            normalTradeQtyMuCrypto = Math.Round(normalTradeQtyMuCrypto / isinParams.MinQty) * isinParams.MinQty;

            //кто-то может хотеть матожидание количества в сделке не меньше, чем.
            decimal newTargetQty = Math.Max(minOrderVolumeCrypto, normalTradeQtyMuCrypto);
            logger.Enqueue($"MinOrderVolumeCrypto={minOrderVolumeCrypto};NormalTradeQtyMuCrypto={normalTradeQtyMuCrypto}. " +
                           $"Chose start NewTargetQty={newTargetQty} for isin {isin}.");

            int newTargetDelayMs = 0;
            while (newTargetDelayMs <= MinTargetDelayMs)
            {
                newTargetQty += isinParams.MinQty;
                int numTrades                 = (int)Math.Round(state.CurrentTargetTurnoverCrypto / newTargetQty);
                if (numTrades == 0) numTrades = 1;
                newTargetDelayMs = msToTheEndOfPeriod / numTrades;
            }

            state.PauseType                 = PauseType.None;
            state.CurrentDoneTurnoverCrypto = 0;
            state.SpanToEndOfPeriod         = spanToEndOfPeriod;
            state.CurrentTargetDelayMs      = newTargetDelayMs;
            state.CurrentTargetQty          = newTargetQty;
            state.CurrentTurnoverPeriod     = newTurnoverPeriod;
            logger.Enqueue($"Successfully set CurrentTurnoverPeriodEnd={state.CurrentTurnoverPeriod};"                                    +
                           $"CurrentTargetTurnoverFiat={newTurnoverFiat};CurrentTargetTurnoverCypto={state.CurrentTargetTurnoverCrypto};" +
                           $"SpanToEndOfPeriod={spanToEndOfPeriod.TotalSeconds}sec;"                                                      +
                           $"TargetDelayMs={state.CurrentTargetDelayMs};TargeQty={state.CurrentTargetQty} for isin {isin}.\n");
            return true;
        }

        TimeSpan ChooseNextDelayMs(IsinVolumeTradingState state)
        {
            string isin = state.Isin;

            if (state.PauseType == PauseType.Stop)
            {
                logger.Enqueue($"STOP trading. Infinite delay for isin {isin}.");
                return Timeout.InfiniteTimeSpan;
            }

            if (state.PauseType == PauseType.Pause)
            {
                //добавляем полсекунды, чтобы точно переползти за время конца периода.
                //чтобы на следующей итерации уже работать со следующим периодом.
                state.SpanToEndOfPeriod = state.SpanToEndOfPeriod.Add(TimeSpan.FromMilliseconds(500));
                logger.Enqueue($"PAUSE trading for {state.SpanToEndOfPeriod.TotalSeconds} seconds for isin {isin}.");
                return state.SpanToEndOfPeriod;
            }

            TradingSpeed lastTradingSpeed = state.LastTradingSpeeds.Count > 0 ? state.LastTradingSpeeds.Last() : TradingSpeed.Average;
            TradingSpeed nextTradingSpeed = ChooseNextTradingSpeed(state, lastTradingSpeed);

            long delayMs;
            switch (nextTradingSpeed)
            {
                case TradingSpeed.Fast:
                    delayMs = ThreadSafeRandom.ThisThreadsRandom.Next(1, 3) * AbsoluteMinDelayMs;
                    break;
                case TradingSpeed.Average:
                    double delayMu    = state.CurrentTargetDelayMs;
                    double delaySigma = state.CurrentTargetDelayMs * AverageTradingSpeedDelaySigmaFrac;

                    //меньше 200мс в average не используем. опять же защита от отрицательных значений.
                    delayMs = Math.Max((int)Math.Round(ThreadSafeRandom.ThisThreadsRandom.NextGaussian(delayMu, delaySigma)), 200);
                    break;
                case TradingSpeed.Slow:

                    //максимум из 40 секунд и средней задержки. если вдруг Average слишком медленный, то Slow будет даже быстрее.
                    //поэтому делаем максимум.
                    int minSlowDelayMs = Math.Max(state.CurrentTargetDelayMs * 2, 40000);
                    delayMs = ThreadSafeRandom.ThisThreadsRandom.Next(minSlowDelayMs, minSlowDelayMs * 3 + 1);
                    break;
                default:
                    logger.Enqueue($"Somehow nextTradingSpeed is {nextTradingSpeed} for isin {isin}. Skip trade.");
                    return TimeSpan.MaxValue;
            }

            string logMessage;
            if (state.OrdersRoundtripMs > 0)
            {
                logMessage = $"Last roundtrip was {state.OrdersRoundtripMs}ms, so decrementing delay={delayMs}ms by roundtrip.";

                if (delayMs - state.OrdersRoundtripMs < AbsoluteMinDelayMs)
                    logMessage += $" Resulting delay is < {AbsoluteMinDelayMs}ms, choose {AbsoluteMinDelayMs}ms as delay.";

                logger.Enqueue(logMessage + $" For isin {isin}.");
                delayMs = Math.Max(AbsoluteMinDelayMs, delayMs - state.OrdersRoundtripMs);
            }

            if (delayMs > context.MaxTradeDelaySec * 1000)
            {
                logger.Enqueue($"Chosen delay={delayMs}ms is more than MaxTradeDelaySec. So choosing MaxTradeDelaySec*1000 as delayMs.");
                delayMs = context.MaxTradeDelaySec * 1000;
            }

            logMessage = $"LastTradingSpeed={lastTradingSpeed}; NextTradingSpeed={nextTradingSpeed}.";
            if (delayMs > 0)
            {
                if (context.HoldBuyOrder || context.HoldSellOrder)
                {
                    decimal multiplier;
                    if (context.HoldBuyOrder && context.HoldSellOrder) multiplier = 0.5m;

                    //умножаем на 0.75, потому что половина задержки будет между сделками. половина по идее должна быть между заявками.
                    //но только в половине(предполагается, что половине) случаев срабатывает нужный режим удержания заявки. поэтому половину,
                    // которая должна быть между заявками тоже делим попопам. и в итогу между сделками 0.5 + 0.25
                    else multiplier = 0.75m;
                    delayMs    =  (long)Math.Round(delayMs * multiplier);
                    logMessage += $" Holding order mode is ON. Delay is multiplied by {multiplier}.";
                }

                logger.Enqueue($"{logMessage} Going to wait for {delayMs}ms for isin {isin}.");
            }
            else
            {
                logger.Enqueue(logMessage + $" Negative delay={delayMs}ms. Skip trade for isin {isin}.");
                return TimeSpan.MaxValue;
            }

            state.LastTradingSpeeds.CircularEnqueue(nextTradingSpeed);

            return TimeSpan.FromMilliseconds(delayMs);
        }

        TradingSpeed ChooseNextTradingSpeed(IsinVolumeTradingState state, TradingSpeed lastTradingSpeed)
        {
            TradingSpeed nextTradingSpeed;

            //возможно фронтранят или ловят. подождём.
            if (state.IsNarrowSpread && context.SlowDownOnNarrowSpread)
            {
                logger.Enqueue($"Due to last seen narrow spread, going to wait. So next trading speed is SLOW. For isin {state.Isin}.");
                return TradingSpeed.Slow;
            }

            //если в последних LastTradingSpeedsWindowSize режимах был Fast или Slow, то их не используем, чтобы не частить.
            //часто повторяться могут только Average.
            var allowedRareEnumValues = new List<TradingSpeed>();
            if (!state.LastTradingSpeeds.Contains(TradingSpeed.Fast)) allowedRareEnumValues.Add(TradingSpeed.Fast);
            if (!state.LastTradingSpeeds.Contains(TradingSpeed.Slow)) allowedRareEnumValues.Add(TradingSpeed.Slow);

            TradingSpeed randomTradingSpeed;

            //наиболее вероятен Average. остальные выбираются равновероятно из allowedRareEnumValues.
            if (ThreadSafeRandom.ThisThreadsRandom.NextDouble() < AverageTradingSpeedProbability || allowedRareEnumValues.Count == 0)
            {
                randomTradingSpeed = TradingSpeed.Average;
            }
            else
            {
                int rndNum = ThreadSafeRandom.ThisThreadsRandom.Next(0, allowedRareEnumValues.Count);
                randomTradingSpeed = allowedRareEnumValues[rndNum];
            }

            switch (lastTradingSpeed)
            {
                //режим Fast подразумевает несколько сделок с малыми промежутками друг за другом.
                //поэтому если предыдущий был Fast, то следующий тоже будет Fast с вероятностью FastTradingSpeedContinuationProbability.
                case TradingSpeed.Fast:
                    nextTradingSpeed = ThreadSafeRandom.ThisThreadsRandom.NextDouble() < FastTradingSpeedContinuationProbability
                                           ? TradingSpeed.Fast
                                           : randomTradingSpeed;
                    break;

                //Average и Slow не подразумевают повторений. поэтому выбираем следующий режим случайно.
                default:
                    nextTradingSpeed = randomTradingSpeed;
                    break;
            }

            if (nextTradingSpeed == TradingSpeed.Slow && !context.EnableSlowDelayMode)
            {
                logger.Enqueue("Slow mode is disabled so replacing nextTradingSpeed with Average.");
                nextTradingSpeed = TradingSpeed.Average;
            }

            return nextTradingSpeed;
        }

        async Task<bool> TryExecuteTrade(IsinVolumeTradingState state, TimeSpan delaySpan)
        {
            string                       isin       = state.Isin;
            SimultaneousTradesIsinParams isinParams = context.IsinsToTrade[isin];
            UnlimitedOrderBook<long>     book       = state.PricesState.Book;

            decimal bid = context.UseVWAP ? book.GetOneSideVwap(OrderSide.Buy,  isinParams.VWAPQty) : book.BestBid;
            decimal ask = context.UseVWAP ? book.GetOneSideVwap(OrderSide.Sell, isinParams.VWAPQty) : book.BestAsk;

            if (!IsBidAskGood(isin, bid, ask, context.UseVWAP, isinParams.VWAPQty)) return false;

            if (!ChooseTradeQty(state, isinParams, out decimal tradeQty)) return false;

            if (!ChooseBuySellConnectors(isinParams, state, tradeQty, ask, out ITradeConnector buyConnector, out ITradeConnector sellConnector)) return false;

            if (!ChooseTradePrice(state, isinParams, book, bid, ask, out decimal tradePrice)) return false;

            state.ClearLastSentOrderData();

            await ChooseSequenceAndSendOrders(state, buyConnector, sellConnector, tradePrice, tradeQty, delaySpan);

            SaveTradeData(state, isinParams.IsReverse.Value, isinParams.LotSize, tradeQty, tradePrice, buyConnector.PublicKey, sellConnector.PublicKey);

            return true;
        }

        void SaveTradeData(IsinVolumeTradingState state,
                           bool                   isReverse,
                           decimal                lotSize,
                           decimal                tradeQty,
                           decimal                tradePrice,
                           string                 buyPublicKey,
                           string                 sellPublicKey)
        {
            string isin = state.Isin;
            state.CurrentDoneTurnoverCrypto += tradeQty;
            state.MyLastTradePrice          =  tradePrice;
            UnlimitedOrderBook<long> conversionBook      = state.ConversionToFiatPricesState.Book;
            decimal                  conversionToFiatMid = (conversionBook.BestBid + conversionBook.BestAsk) / 2;
            decimal thisTradeTurnover =
                TradeModelHelpers.FiatVolumeFromQty(context.IsMarginMarket, isReverse, tradePrice, tradeQty, conversionToFiatMid, lotSize);
            state.LogTurnover(thisTradeTurnover, buyPublicKey, sellPublicKey);
            numSentTrades++;
            logger.Enqueue($"DoneTurnover={state.CurrentDoneTurnoverCrypto};TargetTurnover={state.CurrentTargetTurnoverCrypto};" +
                           $"TotalNumTrades={numSentTrades} for isin {isin}.");
        }

        async Task ChooseSequenceAndSendOrders(IsinVolumeTradingState state,
                                               ITradeConnector        buyConnector,
                                               ITradeConnector        sellConnector,
                                               decimal                tradePrice,
                                               decimal                tradeQty,
                                               TimeSpan               delaySpan)
        {
            string timestamp         = DateTime.UtcNow.ToString("MMdd-HHmmss.fff");
            string buyClientOrderId  = $"{state.Isin}_{timestamp}_B_{orderIdGenerator.Id}";
            string sellClientOrderId = $"{state.Isin}_{timestamp}_S_{orderIdGenerator.Id}";

            logger.Enqueue($"Going to send buyOrder: id={buyClientOrderId};price={tradePrice};qty={tradeQty} " +
                           $"and sellOrder: id={sellClientOrderId};price={tradePrice};qty={tradeQty} for isin {state.Isin}.");
            state.AddNewOrderId(buyClientOrderId);
            state.AddNewOrderId(sellClientOrderId);

            state.StartMeasuringRoundtrip();

            //Если цена растёт, то в списке сделок пусть эта сделка окрашивается зелёным, что мол покупают. если цена падает, то продают.
            //Чтобы сделка была покупкой, то покупка должна быть агрессивной, то есть сначала надо выставить продажу.
            //При этом в режиме SafeBuySideMode всегда выставляем сначала покупку.
            if (tradePrice >= state.MyLastTradePrice && !context.SafeBuySideMode)
            {
                await ImmediateOrHoldSendOrder(state,
                                               sellConnector,
                                               buyConnector,
                                               sellClientOrderId,
                                               buyClientOrderId,
                                               OrderSide.Sell,
                                               OrderSide.Buy,
                                               "SELL",
                                               "BUY",
                                               tradePrice,
                                               tradeQty,
                                               delaySpan);
            }
            else
            {
                await ImmediateOrHoldSendOrder(state,
                                               buyConnector,
                                               sellConnector,
                                               buyClientOrderId,
                                               sellClientOrderId,
                                               OrderSide.Buy,
                                               OrderSide.Sell,
                                               "BUY",
                                               "SELL",
                                               tradePrice,
                                               tradeQty,
                                               delaySpan);
            }
        }

        async Task ImmediateOrHoldSendOrder(IsinVolumeTradingState state,
                                            ITradeConnector        firstConnector,
                                            ITradeConnector        secondConnector,
                                            string                 firstClientOrderId,
                                            string                 secondClientOrderId,
                                            OrderSide              firstSide,
                                            OrderSide              secondSide,
                                            string                 firstSideString,
                                            string                 secondSideString,
                                            decimal                tradePrice,
                                            decimal                tradeQty,
                                            TimeSpan               delaySpan)
        {
            if (context.HoldBuyOrder || context.HoldSellOrder)
            {
                await SendOrders(state,
                                 firstConnector,
                                 secondConnector,
                                 firstClientOrderId,
                                 secondClientOrderId,
                                 firstSide,
                                 secondSide,
                                 firstSideString,
                                 secondSideString,
                                 tradePrice,
                                 tradeQty,
                                 delaySpan);
            }
            else
                await PrepareAndSendOrdersPair(state,
                                               firstConnector,
                                               secondConnector,
                                               firstClientOrderId,
                                               secondClientOrderId,
                                               firstSide,
                                               secondSide,
                                               firstSideString,
                                               secondSideString,
                                               tradePrice,
                                               tradeQty);
        }

        async Task SendOrders(IsinVolumeTradingState state,
                              ITradeConnector        firstConnector,
                              ITradeConnector        secondConnector,
                              string                 firstClientOrderId,
                              string                 secondClientOrderId,
                              OrderSide              firstSide,
                              OrderSide              secondSide,
                              string                 firstSideString,
                              string                 secondSideString,
                              decimal                tradePrice,
                              decimal                tradeQty,
                              TimeSpan               delaySpan)
        {
            string isin = state.Isin;
            state.LastSentOrderId = firstClientOrderId;
            firstConnector.AddOrder(firstClientOrderId, isin, firstSide, tradePrice, tradeQty, requestIdGenerator.Id);
            logger.Enqueue($"Sent {firstSideString} order with id={firstClientOrderId} for isin {isin}.");

            if (firstSide == OrderSide.Buy && context.HoldBuyOrder || firstSide == OrderSide.Sell && context.HoldSellOrder)
            {
                logger.Enqueue($"Going to wait for {delaySpan.TotalMilliseconds}ms before next send for isin {isin}.");
                state.PauseMeasuringRoundtrip();
                await Task.Delay(delaySpan, childSource.Token);
                state.ResumeMeasuringRoundtrip();
            }

            if (!state.LastSentOrderExecuted)
            {
                secondConnector.AddOrder(secondClientOrderId, isin, secondSide, tradePrice, tradeQty, requestIdGenerator.Id);
                logger.Enqueue($"Sent {secondSideString} order with id={secondClientOrderId} for isin {isin}.");
            }
            else
            {
                logger.Enqueue($"{firstSideString} order with id={firstClientOrderId} was executed. Not sending {secondSideString} order. For isin {isin}.");

                //заявка исполнилась. можно больше ничего не ждать.
                state.ClearSentOrderIds();
                state.SetNewOrdersEvent();
                state.SetExecutionReportsEvent();
                state.StoreRoundtrip();
            }
        }

        async Task PrepareAndSendOrdersPair(IsinVolumeTradingState state,
                                            ITradeConnector        firstConnector,
                                            ITradeConnector        secondConnector,
                                            string                 firstClientOrderId,
                                            string                 secondClientOrderId,
                                            OrderSide              firstSide,
                                            OrderSide              secondSide,
                                            string                 firstSideString,
                                            string                 secondSideString,
                                            decimal                tradePrice,
                                            decimal                tradeQty)
        {
            string isin = state.Isin;

            firstConnector.PrepareOrder(firstClientOrderId, isin, firstSide, tradePrice, tradeQty, requestIdGenerator.Id);
            secondConnector.PrepareOrder(secondClientOrderId, isin, secondSide, tradePrice, tradeQty, requestIdGenerator.Id);
            logger.Enqueue($"Prepared {firstSideString} order with id={firstClientOrderId} and " +
                           $"{secondSideString} order with id={secondClientOrderId} for isin {isin}.");

            await Task.WhenAll(new List<Task> {firstConnector.SendPreparedOrder(), secondConnector.SendPreparedOrder()});
            logger.Enqueue($"Sent {firstSideString} order with id={firstClientOrderId} and " +
                           $"{secondSideString} order with id={secondClientOrderId} for isin {isin}.");
        }

        bool ChooseBuySellConnectors(BaseIsinParams         isinParams,
                                     IsinVolumeTradingState isinState,
                                     decimal                tradeQty,
                                     decimal                ask,
                                     out ITradeConnector    buyConnector,
                                     out ITradeConnector    sellConnector)
        {
            string isin                      = isinParams.Isin;
            bool   connectorsChoosingSuccess = false;
            buyConnector  = null;
            sellConnector = null;
            for (int i = 0; i < 5 && !connectorsChoosingSuccess; i++)
            {
                logger.Enqueue($"Try {i} to choose connectors.");
                connectorsChoosingSuccess = TryOnceChooseBuySellConnectors(isinParams, isinState, tradeQty, ask, out buyConnector, out sellConnector);
                if (connectorsChoosingSuccess)
                    logger.Enqueue($"Chose buyConnector={buyConnector.Name} " + $"sellConnector={sellConnector.Name} for isin {isin}.");
            }

            if (!connectorsChoosingSuccess) return false;
            return true;
        }

        bool IsBidAskGood(string isin, decimal bid, decimal ask, bool useVWAP, decimal vwapQty)
        {
            string logmessage = useVWAP ? $"Using VWAP with vwapQty={vwapQty}. " : "Using BEST. ";
            if (bid >= ask)
            {
                logger.Enqueue(logmessage + $"Best prices are crossed for {isin} book. Bid={bid};Ask={ask}. Skip trade.");
                return false;
            }

            logger.Enqueue(logmessage + $"Current bid={bid};ask={ask} for isin {isin}.");
            return true;
        }

        bool ChooseTradeQty(IsinVolumeTradingState state, SimultaneousTradesIsinParams isinParams, out decimal tradeQty)
        {
            string  isin    = state.Isin;
            decimal baseQty = state.CurrentTargetQty;
            double  sigma   = (double)(baseQty * context.BaseQtySigmaFrac);

            //Редко включаем режим больших объёмов и не в быстром режиме.
            if (context.EnableHighVolumeMode                                         &&
                state.LastTradingSpeeds.Last()                  != TradingSpeed.Fast &&
                ThreadSafeRandom.ThisThreadsRandom.NextDouble() < HighVolumeProbability)
            {
                baseQty *= 100;
                sigma   =  (double)(baseQty * context.BaseQtySigmaFrac);
                logger.Enqueue($"Mighty random says that this trade is gonna be high volume with baseQty={baseQty};sigma={sigma} for isin {isin}.");
            }

            //допускаются отрицательные значения. просто не будем торговать.
            tradeQty = 0;
            while (tradeQty == 0) tradeQty = (decimal)ThreadSafeRandom.ThisThreadsRandom.NextGaussian((double)baseQty, sigma);

            //иногда нужно округлить объём
            decimal numToRoundTo;
            if (ThreadSafeRandom.ThisThreadsRandom.NextDouble() < RoundTradeQtyProbability)
            {
                numToRoundTo = ThreadSafeRandom.ThisThreadsRandom.Next(0, 2) == 0 ? isinParams.LargeRoundValue : isinParams.SmallRoundValue;
                logger.Enqueue($"Gonna round this tradeQty={tradeQty} to {numToRoundTo} for isin {isin}.");
            }
            else numToRoundTo = isinParams.MinQty;

            //minQty вполне может быть > LargeRoundValue.
            numToRoundTo = Math.Max(numToRoundTo, isinParams.MinQty);
            tradeQty     = Math.Round(tradeQty / numToRoundTo) * numToRoundTo;

            //проверка на минимальное количество
            UnlimitedOrderBook<long> book = state.PricesState.Book;
            decimal                  bid  = context.UseVWAP ? book.GetOneSideVwap(OrderSide.Buy,  isinParams.VWAPQty) : book.BestBid;
            decimal                  ask  = context.UseVWAP ? book.GetOneSideVwap(OrderSide.Sell, isinParams.VWAPQty) : book.BestAsk;
            decimal                  mid  = (bid + ask) / 2;

            decimal minOrderVolumeCrypto =
                TradeModelHelpers.QtyFromCurrencyVolume(context.IsMarginMarket, isinParams.IsReverse.Value, isinParams.MinOrderVolume, mid, isinParams.LotSize);
            minOrderVolumeCrypto = Math.Round(minOrderVolumeCrypto / isinParams.MinQty) * isinParams.MinQty;

            if (tradeQty < minOrderVolumeCrypto || tradeQty <= 0)
            {
                logger.Enqueue($"Chosen tradeQty={tradeQty} is < minOrderVolumeCrypto={minOrderVolumeCrypto} or <= 0 for isin {isin}. Skip trade.");
                return false;
            }

            logger.Enqueue($"Chose tradeQty={tradeQty} for isin {isin}.");
            return true;
        }

        bool TryOnceChooseBuySellConnectors(BaseIsinParams         isinParams,
                                            IsinVolumeTradingState isinState,
                                            decimal                tradeQty,
                                            decimal                ask,
                                            out ITradeConnector    buyConnector,
                                            out ITradeConnector    sellConnector)
        {
            string isin = isinParams.Isin;
            buyConnector  = null;
            sellConnector = null;
            string  buyMarginCurrency  = isinParams.BuyMarginCurrency;
            string  sellMarginCurrency = isinParams.SellMarginCurrency;
            decimal convertToFiatMid   = (isinState.ConversionToFiatPricesState.Book.BestBid + isinState.ConversionToFiatPricesState.Book.BestAsk) / 2;

            var buyAccountStates = new List<AccountState>();
            foreach (AccountState state in accountStates.Values)
            {
                if (!state.TryGetBalance(buyMarginCurrency, out decimal balance)) continue;
                state.TryGetMarginPosition(isin, out decimal position);
                if (IsEnoughBalance(OrderSide.Buy, balance, position)) buyAccountStates.Add(state);
            }

            if (buyAccountStates.Count == 0)
            {
                logger.Enqueue($"None of accounts have enough balance to buy {tradeQty} {isin}. Skip trade.");
                gotBalances = false;
                return false;
            }

            if (context.IsMarginMarket)
            {
                //выбираем коннектор с наименьшей позицией. будем её выкупать.
                buyConnector = buyAccountStates.MinBy(state => state.TryGetMarginPosition(isin, out decimal position) ? position : 0).Connector;
            }
            else buyConnector = buyAccountStates.RandomElement(ThreadSafeRandom.ThisThreadsRandom).Connector;

            var sellAccountStates = new List<AccountState>();
            foreach (AccountState state in accountStates.Values)
            {
                if (!state.TryGetBalance(sellMarginCurrency, out decimal balance)) continue;
                state.TryGetMarginPosition(isin, out decimal position);
                if (IsEnoughBalance(OrderSide.Sell, balance, position) && state.Connector != buyConnector) sellAccountStates.Add(state);
            }

            if (sellAccountStates.Count == 0)
            {
                logger.Enqueue($"None of accounts (excluding account with {buyConnector.Name} connector which was chosen to buy) " +
                               $"have enough balance to sell {tradeQty} {isin}. Skip trade.");
                gotBalances = false;
                return false;
            }

            if (context.IsMarginMarket)
            {
                //выбираем коннектор с наибольшей позицией. будем её продавать.
                sellConnector = sellAccountStates.MaxBy(state => state.TryGetMarginPosition(isin, out decimal position) ? position : 0).Connector;
            }
            else sellConnector = sellAccountStates.RandomElement(ThreadSafeRandom.ThisThreadsRandom).Connector;

            return true;

            bool IsEnoughBalance(OrderSide side, decimal balance, decimal position)
            {
                bool isBalanceEnough = TradeModelHelpers.IsEnoughBalance(context.IsMarginMarket,
                                                                         isinParams.IsReverse.Value,
                                                                         ask,
                                                                         tradeQty,
                                                                         convertToFiatMid,
                                                                         isinParams.LotSize,
                                                                         isinParams.Leverage,
                                                                         side,
                                                                         balance);

                //bool isGoingToCloseMarginPos = (side == OrderSide.Buy ? position < 0 : position > 0) && Math.Abs(position) > tradeQty;

                return isBalanceEnough; //|| context.IsMarginMarket && isGoingToCloseMarginPos;
            }
        }

        bool ChooseTradePrice(IsinVolumeTradingState       state,
                              SimultaneousTradesIsinParams isinParams,
                              UnlimitedOrderBook<long>     book,
                              decimal                      bid,
                              decimal                      ask,
                              out decimal                  tradePrice)
        {
            string isin = state.Isin;
            tradePrice = -1;
            var     basePrices         = new List<decimal>();
            decimal minBasePriceOffset = context.MinBasePriceOffsetFromBestMinsteps * isinParams.MinStep;

            if (!ChooseBasePrice(state, isin, minBasePriceOffset, isinParams.MinStep, bid, ask, basePrices, out decimal basePrice)) return false;

            double sigma           = (double)Math.Min(basePrice - bid - minBasePriceOffset, ask - basePrice - minBasePriceOffset) * PriceBestOffsetSigmaFrac;
            double randomizedPrice = ThreadSafeRandom.ThisThreadsRandom.NextGaussian((double)basePrice, sigma);
            tradePrice = (decimal)Math.Round(randomizedPrice / (double)isinParams.MinStep) * isinParams.MinStep;
            logger.Enqueue($"Got tradePrice={tradePrice} using basePrice={basePrice} and sigma={sigma} for isin {isin}.");

            //ещё одна проверка цены на всякий случай
            bid = context.UseVWAP ? book.GetOneSideVwap(OrderSide.Buy,  isinParams.VWAPQty) : book.BestBid;
            ask = context.UseVWAP ? book.GetOneSideVwap(OrderSide.Sell, isinParams.VWAPQty) : book.BestAsk;
            if (tradePrice < bid + minBasePriceOffset || tradePrice > ask - minBasePriceOffset)
            {
                logger.Enqueue("Book prices have changed while calculations were made or tradePrice is outside boundaries. Using "     +
                               (context.UseVWAP ? "VWAP. " : "BEST. ")                                                                 +
                               $"Anyway now tradePrice={tradePrice} is outside the spread narrowed with offset={minBasePriceOffset}: " +
                               $"bid={bid};ask={ask} for isin {isin}. Skip trade.");
                return false;
            }

            return true;
        }

        bool ChooseBasePrice(IsinVolumeTradingState state,
                             string                 isin,
                             decimal                minBasePriceOffset,
                             decimal                minStep,
                             decimal                bid,
                             decimal                ask,
                             List<decimal>          basePrices,
                             out decimal            basePrice)
        {
            decimal myLastTradePrice = state.MyLastTradePrice;
            decimal last             = state.PricesState.LastPrice;
            basePrice = 0;
            decimal midprice = (ask + bid) / 2;
            decimal spread   = ask - bid;

            //почему 2 * minBasePriceOffset? пример: minBasePriceOffset=1. bid=100 ask=102 => spread=2. можно торговать по 101 и всё.
            //при меньшем спрэде торговать нельзя.
            if (spread <= minStep || spread < 2 * minBasePriceOffset)
            {
                logger.Enqueue($"Spread={spread} is too narrow. Minstep={minStep}, double minBasePriceOffset={2 * minBasePriceOffset}. " +
                               $"Not using midprice and quarters as base prices for {isin}.");
                state.IsNarrowSpread = true;
                return false;
            }

            state.IsNarrowSpread = false;

            if (context.SafeBuySideMode)
            {
                decimal lowerQuarter       = (midprice + bid) / 2;
                int     quarterRange       = (int)Math.Round((lowerQuarter - bid - minBasePriceOffset) / minStep);
                int     priceRangeMinsteps = Math.Min(quarterRange, MinSafeBuyModeRangeFromBidMinsteps);

                if (priceRangeMinsteps <= 2)
                {
                    logger.Enqueue($"SafeBuySideMode is ON, but priceRangeMinsteps={priceRangeMinsteps}<=2. " +
                                   $"LowerQuarter={lowerQuarter}. Skip trade for isin {isin}.");
                    return false;
                }

                //В этом режиме торгуем от покупки в нижней четверти спрэда. Вносим некоторую случайность.
                decimal randomPart = ThreadSafeRandom.ThisThreadsRandom.Next(0, priceRangeMinsteps + 1) * minStep;
                basePrice = bid + minBasePriceOffset + randomPart;
                logger.Enqueue($"SafeBuySideMode is ON, so chosen basePrice={basePrice} as " +
                               $"bid={bid} + minBasePriceOffset={minBasePriceOffset} + random={randomPart} for isin {isin}.");
            }

            //Большую часть времени крутимся вокруг предыдущей цены сделки, если она внутри спрэда с допусками, чтобы не скакать туда-сюда.
            //Потом перескакиваем и опять крутимся.
            else if (myLastTradePrice                                > 0                         &&
                     myLastTradePrice                                >= bid + minBasePriceOffset &&
                     myLastTradePrice                                <= ask - minBasePriceOffset &&
                     ThreadSafeRandom.ThisThreadsRandom.NextDouble() < ChoosePreviousBasePriceProbability)
            {
                basePrice = myLastTradePrice;
                logger.Enqueue($"Mighty random says that MyLastTradePrice={myLastTradePrice} is chosen as a basePrice for isin {isin}.");
            }
            else
            {
                //Иначе случайно выбираем из цены последней сделки на рынке, мидпрайса и (bid + mid) / 2  и (ask + mid) /2
                TryAddLastToBasePrices(last, isin, basePrices, bid, ask, minBasePriceOffset);
                TryAddMidAndQuartersToBasePrices(isin, basePrices, bid, ask, minBasePriceOffset, minStep);

                if (basePrices.Count == 0)
                {
                    logger.Enqueue($"Couldn't choose any base price for {isin}. Skip trade.");
                    basePrice = -1;
                    return false;
                }

                basePrice = basePrices.RandomElement(ThreadSafeRandom.ThisThreadsRandom);
            }

            return true;
        }

        void TryAddLastToBasePrices(decimal last, string isin, List<decimal> basePrices, decimal bid, decimal ask, decimal minBasePriceOffset)
        {
            //не будем использовать ласт, если он уже близко к бесту или за спредом. условие выполнится, если на найдём ласт в dictionary.
            if (last >= bid + minBasePriceOffset && last <= ask - minBasePriceOffset)
            {
                basePrices.Add(last);
                logger.Enqueue($"Last={last} seems good so gonna use it as of af base prices for {isin}.");
            }
            else logger.Enqueue($"Last={last} is too close to bid={bid} or ask={ask} or even beyond spread. Not using it as a base price for {isin}.");
        }

        void TryAddMidAndQuartersToBasePrices(string isin, List<decimal> basePrices, decimal bid, decimal ask, decimal minBasePriceOffset, decimal minStep)
        {
            decimal midprice = (ask + bid) / 2;
            decimal spread   = ask - bid;

            if (spread / midprice * 100 <= context.MaxSpreadToChooseMidPricePerc)
            {
                decimal higherQuarter = (ask + midprice) / 2;
                decimal lowerQuarter  = (bid + midprice) / 2;

                string logmessage = $"Spread={spread} seems good so gonna use ";
                if (lowerQuarter >= bid + minBasePriceOffset)
                {
                    basePrices.Add(lowerQuarter);
                    logmessage += $"lowerQuarter={lowerQuarter};";
                }

                basePrices.Add(midprice);
                logmessage += $"midprice={midprice}";

                if (higherQuarter <= ask - minBasePriceOffset)
                {
                    basePrices.Add(higherQuarter);
                    logmessage += $";higherQuarter={higherQuarter}";
                }

                logger.Enqueue(logmessage + $" as base prices for {isin}.");
            }
            else logger.Enqueue($"Spread={spread} is too wide. Not using midprice and quarters as base prices for {isin}.");
        }

        async Task CancelAllOrders(bool beforeExit = false)
        {
            foreach (AccountState state in accountStates.Values)
            {
                //Console.WriteLine($"Requesting active orders for connector {state.Connector.Name} {Thread.CurrentThread.ManagedThreadId}");
                var tradeConnector = state.Connector;
                tradeConnector.GetActiveOrders(requestIdGenerator.Id);

                await state.WaitForActiveOrdersAsync(WaitingTimeoutMs, state.Connector.Name, beforeExit);
            }
        }

        async Task GetPosAndMoney()
        {
            foreach (AccountState state in accountStates.Values)
            {
                //Console.WriteLine($"Requesting balance for connector {state.Connector.Name} {Thread.CurrentThread.ManagedThreadId}");
                var tradeConnector = state.Connector;
                tradeConnector.GetPosAndMoney(requestIdGenerator.Id);

                await state.WaitForBalance(WaitingTimeoutMs, state.Connector.Name);
            }
        }

        #region Connector event handlers
        void Connector_Connected(object sender, EventArgs e)
        {
            var connector = (IDataConnector)sender;

            if (areDataConnectorsConnected.ContainsKey(connector.Name)) areDataConnectorsConnected[connector.Name] = true;
            else accountStates[connector.Name].IsConnected                                                         = true;

            //Console.WriteLine($"{connector.Name} connected.  {Thread.CurrentThread.ManagedThreadId}");

            if (accountStates.Values.All(state => state.IsConnected)              &&
                areDataConnectorsConnected.Values.All(isConnected => isConnected) &&
                !connectedResetEvent.IsSet)
            {
                //Console.WriteLine($"{connector.Name} trying to set event.  {Thread.CurrentThread.ManagedThreadId}");
                connectedResetEvent.Set();

                //Console.WriteLine($"{connector.Name} event set.  {Thread.CurrentThread.ManagedThreadId}");
            }

            logger.Enqueue($"{connector.Name} socket connected.");
        }

        void Connector_Disconnected(object sender, EventArgs e)
        {
            var connector = (IDataConnector)sender;

            if (areDataConnectorsConnected.ContainsKey(connector.Name)) areDataConnectorsConnected[connector.Name] = false;
            else accountStates[connector.Name].IsConnected                                                         = false;

            logger.Enqueue($"{connector.Name} socket disconnected.");

            if (isinsByConnector.TryGetValue(connector.Name, out List<string> isins))
            {
                logger.Enqueue($"Going to reset BookCrossStartedTimestamps for all isins: {string.Join(';', isins)} for connector {connector.Name}.");
                foreach (string isin in isins)
                {
                    if (!pricesStates.TryGetValue($"{connector.ExchangeName}_{isin}", out PricesState state)) return;

                    string lastBookCrossStartedTimestampString = state.BookBrokenStartedTimestamp.ToString("HH:mm:ss.fff");
                    state.ResetBookBrokenStartedTimestamp();
                    logger.Enqueue($"Reset BookCrossStartedTimestamp={lastBookCrossStartedTimestampString} for isin {isin} for connector {connector.Name}.");
                }
            }

            connector.Start();
        }

        void Connector_BookSnapshotArrived(object sender, BookMessage bookMessage)
        {
            var connector = (IDataConnector)sender;

            if (!pricesStates.TryGetValue($"{connector.ExchangeName}_{bookMessage.Isin}", out PricesState state)) return;

            BookHelpers.ApplySnapshot(bookMessage, state.Book, NumBookErrorsInWindowToThrow);

            TradeModelHelpers.TryRestartConnectorOnBrokenData(bookMessage,
                                                              state,
                                                              connector,
                                                              logger,
                                                              tradeDataLocker,
                                                              childSource,
                                                              ReconnectOnCrossedBookTimeoutSec,
                                                              WaitingTimeoutMs);
        }

        void Connector_BookUpdateArrived(object sender, BookMessage bookMessage)
        {
            var connector = (IDataConnector)sender;

            if (!pricesStates.TryGetValue($"{connector.ExchangeName}_{bookMessage.Isin}", out PricesState state)) return;

            BookHelpers.ApplyUpdate(bookMessage, state.Book, NumBookErrorsInWindowToThrow, state.CheckBookCross, logger);

            TradeModelHelpers.TryRestartConnectorOnBrokenData(bookMessage,
                                                              state,
                                                              connector,
                                                              logger,
                                                              tradeDataLocker,
                                                              childSource,
                                                              ReconnectOnCrossedBookTimeoutSec,
                                                              WaitingTimeoutMs);
        }

        void Connector_TickerArrived(object sender, TickerMessage tickerMessage)
        {
            var connector = (IDataConnector)sender;

            if (!pricesStates.TryGetValue($"{connector.ExchangeName}_{tickerMessage.Isin}", out PricesState state)) return;

            //state.Bid = tickerMessage.Bid;
            //state.Ask = tickerMessage.Ask;
            state.LastPrice = tickerMessage.Last;
        }

        void Connector_ActiveOrdersListArrived(object sender, List<OrderMessage> activeOrders)
        {
            var connector = (ITradeConnector)sender;

            //Console.WriteLine($"Got active orders for connector {connector.Name}. Connector handler. {Thread.CurrentThread.ManagedThreadId}");

            foreach (OrderMessage order in activeOrders)
            {
                logger.Enqueue($"Active order: {order} in connector {connector.Name}. Trying to cancel.");
                connector.CancelOrder(order.OrderId, requestIdGenerator.Id);
            }

            accountStates[connector.Name].SetActiveOrdersEvent();
        }

        void Connector_NewOrderAdded(object sender, OrderMessage newOrder)
        {
            logger.Enqueue($"New order: {newOrder}");

            if (!isinTradingStates.TryGetValue(newOrder.Isin, out IsinVolumeTradingState state)) return;

            if (state.TryRemoveLastNewResponseOrderId(newOrder.OrderId)) state.SetNewOrdersEvent();
        }

        void Connector_OrderCanceled(object sender, OrderMessage canceledOrder)
        {
            logger.Enqueue($"Canceled order: {canceledOrder}");
        }

        void Connector_OrderReplaced(object sender, OrderMessage replacedOrder)
        {
            logger.Enqueue($"Replaced order: {replacedOrder}");
        }

        void Connector_ExecutionReportArrived(object sender, OrderMessage report)
        {
            logger.Enqueue($"Execution report: {report}");

            var          connector    = (ITradeConnector)sender;
            AccountState accountState = accountStates[connector.Name];
            if (!isinTradingStates.TryGetValue(report.Isin, out IsinVolumeTradingState isinState)) return;
            if (!context.IsinsToTrade.TryGetValue(report.Isin, out SimultaneousTradesIsinParams isinToTrade)) return;

            string  buyCurrency  = isinToTrade.BuyMarginCurrency;
            string  sellCurrency = isinToTrade.SellMarginCurrency;
            bool    buyUpdateSuccess;
            bool    sellUpdateSuccess;
            decimal newBuyBalance;
            decimal newSellBalance;

            if (context.IsMarginMarket)
            {
                decimal convertToFiatMid = (isinState.ConversionToFiatPricesState.Book.BestBid + isinState.ConversionToFiatPricesState.Book.BestAsk) / 2;
                decimal marginPosDiff    = accountState.GetMarginPosDiffAndUpdate(report.Isin, report.Side, report.TradeQty);

                //подразумеваем, что для marginMarket buyCurrency=sellCurrency
                decimal balanceDiff = TradeModelHelpers.MarginVolumeFromQty(true,
                                                                            isinToTrade.IsReverse.Value,
                                                                            report.Price,
                                                                            marginPosDiff,
                                                                            convertToFiatMid,
                                                                            isinToTrade.LotSize,
                                                                            isinToTrade.Leverage,
                                                                            OrderSide.Buy);
                buyUpdateSuccess  = TryUpdateAndCheck(buyCurrency, balanceDiff, out newBuyBalance);
                sellUpdateSuccess = buyUpdateSuccess;
                newSellBalance    = newBuyBalance;
            }
            else
            {
                int     increaseBuyBalanceCoef = report.Side == OrderSide.Buy ? -1 : 1;
                decimal buyBalanceDiff         = increaseBuyBalanceCoef * report.TradeQty * report.Price - report.TradeFee;
                decimal sellBalanceDiff        = -1 * increaseBuyBalanceCoef * report.TradeQty;

                buyUpdateSuccess  = TryUpdateAndCheck(buyCurrency,  buyBalanceDiff,  out newBuyBalance);
                sellUpdateSuccess = TryUpdateAndCheck(sellCurrency, sellBalanceDiff, out newSellBalance);
            }

            logger.Enqueue($"New {(buyUpdateSuccess ? $"{buyCurrency} balance={newBuyBalance};" : "")}" +
                           $"{(sellUpdateSuccess ? $"{sellCurrency} balance={newSellBalance}" : "")}"   +
                           $" for account with connector name {connector.Name}.");

            if (report.OrderId == isinState.LastSentOrderId) isinState.LastSentOrderExecuted = true;
            if (isinState.TryRemoveLastExecutionReportOrderId(report.OrderId))
            {
                isinState.StoreRoundtrip();
                isinState.SetExecutionReportsEvent();
            }

            bool TryUpdateAndCheck(string currency, decimal balanceDiff, out decimal newBalance)
            {
                newBalance = decimal.MinValue;
                bool updateSuccess = accountState.TryUpdateBalance(currency, balanceDiff, out newBalance);
                if (updateSuccess && newBalance < 0) PrintBalanceError(currency);

                return updateSuccess;
            }

            void PrintBalanceError(string currency)
            {
                logger.Enqueue($"Error. Calculations state that new {currency} balance is negative for account with connector name {connector.Name}");
            }
        }

        void Connector_BalanceArrived(object sender, List<BalanceMessage> balances)
        {
            var connector = (ITradeConnector)sender;

            //Console.WriteLine($"Got balances for connector {connector.Name}. Connector handler. {Thread.CurrentThread.ManagedThreadId}");
            AccountState accountState = accountStates[connector.Name];
            foreach (BalanceMessage balance in balances.Where(balance => usedCurrencies.Contains(balance.Currency)))
            {
                accountState.AvailableSpotBalances[balance.Currency] = balance.Available;
                logger.Enqueue($"Balance: {balance} for connector {connector.Name}");
            }

            accountState.SetBalanceEvent();
        }

        void Connector_PositionArrived(object sender, List<PositionMessage> positions)
        {
            var connector = (ITradeConnector)sender;

            AccountState accountState    = accountStates[connector.Name];
            var          positionsByIsin = new Dictionary<string, decimal>();

            foreach (PositionMessage position in positions)
            {
                if (positionsByIsin.ContainsKey(position.Isin)) positionsByIsin[position.Isin] += position.Qty;
                else positionsByIsin[position.Isin]                                            =  position.Qty;

                logger.Enqueue($"Individual position: {position} for connector {connector.Name}");
            }

            foreach (KeyValuePair<string, decimal> pair in positionsByIsin)
            {
                string  isin     = pair.Key;
                decimal position = pair.Value;
                accountState.UpdateMarginPosition(isin, position);
                logger.Enqueue($"Sum position for isin {isin}={position} for connector {connector.Name}");
            }
        }

        void Connector_ErrorOccured(object sender, ErrorMessage error)
        {
            var connector = (ITradeConnector)sender;

            logger.Enqueue($"Recoverable error. Code={(RequestError)error.Code}. Message={error.Message}. Description={error.Description}. " +
                           $"Connector={connector.ExchangeName}_{connector.Name}");

            AccountState accountState = accountStates[connector.Name];

            switch (error.Code)
            {
                case (int)RequestError.ActiveOrders:
                    accountState.SetActiveOrdersEvent();
                    break;

                case (int)RequestError.TradingBalance:
                    accountState.SetBalanceEvent();
                    break;

                case (int)RequestError.AddOrder:
                    foreach (IsinVolumeTradingState state in isinTradingStates.Values)
                    {
                        state.ClearNewResponseOrderIds();
                        state.SetNewOrdersEvent();
                    }

                    break;
            }

            gotBalances = false;
        }
        #endregion
    }
}
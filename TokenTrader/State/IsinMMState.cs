using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SharedTools;
using SharedTools.Interfaces;
using TokenTrader.DataStructures;
using TokenTrader.Initialization;
using TokenTrader.TradeModels;

namespace TokenTrader.State
{
    class IsinMMState
    {
        readonly FormulaContext       baseFormula;
        readonly List<FormulaContext> predictorFormulas;
        readonly ILogger              logger;
        readonly decimal              fullSideDealShift;
        readonly decimal              sumOneSideVolumeFiat;
        readonly decimal              buyPotentialLimitFiat;
        readonly decimal              sellPotentialLimitFiat;
        readonly bool                 isMarginMarket;
        readonly decimal              lotSize;
        readonly bool                 isReverse;
        readonly StreamWriter         tradesWriter;
        readonly int                  waitPendingTimeoutMs;

        readonly SortedList<decimal, Dictionary<string, OrderMessage>> activeBids =
            new SortedList<decimal, Dictionary<string, OrderMessage>>(new ReverseComparer<decimal>());

        readonly SortedList<decimal, Dictionary<string, OrderMessage>> activeAsks          = new SortedList<decimal, Dictionary<string, OrderMessage>>();
        readonly Dictionary<string, OrderMessage>                      allActiveOrdersById = new Dictionary<string, OrderMessage>();

        readonly MultiKeyDictionary<decimal, string, OrderState> bidStates            = new MultiKeyDictionary<decimal, string, OrderState>();
        readonly MultiKeyDictionary<decimal, string, OrderState> askStates            = new MultiKeyDictionary<decimal, string, OrderState>();
        readonly ConcurrentDictionary<string, OrderMessage>      lostObligationOrders = new ConcurrentDictionary<string, OrderMessage>();

        readonly object    activeOrdersLocker        = new object();
        readonly object    positionLocker            = new object();
        readonly Stopwatch ordersActionDelayMeasurer = Stopwatch.StartNew();

        decimal lastUnderlyingBid;
        decimal lastUnderlyingAsk;
        int     delayToNextOrdersActionMs;
        decimal dealShift;
        decimal positionFiat;

        public PricesState     ConversionToFiatPricesState   { get; }
        public PricesState     HedgePricesState              { get; private set; }
        public AccountState    TradeAccountState             { get; }
        public AccountState    HedgeAccountState             { get; private set; }
        public ITradeConnector TradeConnector                { get; }
        public bool            PredictorsCoefsSet            { get; set; }
        public bool            DelayToNextOrdersActionPassed => ordersActionDelayMeasurer.ElapsedMilliseconds >= delayToNextOrdersActionMs;
        public bool            FormulasValuesSet             => baseFormula.ValuesSet && predictorFormulas.All(formula => formula.ValuesSet);
        public string          Isin                          { get; }

        public string NotSetFormulas =>
            $"{(!baseFormula.ValuesSet ? baseFormula.ExpressionString + ";" : "")}{string.Join(',', predictorFormulas.Where(formula => !formula.ValuesSet).Select(formula => formula.ExpressionString))}";

        public bool ShouldHedge { get; private set; }
        public bool ShouldStopOnHedgeCancel { get; private set; }

        public decimal PositionFiat
        {
            get
            {
                lock (positionLocker) return positionFiat;
            }
        }

        //bool AllBuysTraded => dealShift <= -1 * fullSideDealShift;
        //bool AllSellsTraded => dealShift >= fullSideDealShift;

        public IsinMMState(FormulaContext       baseFormula,
                           List<FormulaContext> predictorFormulas,
                           PricesState          conversionToFiatPricesState,
                           AccountState         tradeAccountState,
                           ILogger              logger,
                           string               isin,
                           decimal              positionFiat,
                           decimal              fullSideDealShift,
                           decimal              sumOneSideVolumeFiat,
                           decimal              buyPotentialLimitFiat,
                           decimal              sellPotentialLimitFiat,
                           bool                 isMarginMarket,
                           decimal              lotSize,
                           bool                 isReverse,
                           StreamWriter         tradesWriter,
                           int                  waitPendingTimeoutMs)
        {
            this.baseFormula            = baseFormula;
            this.predictorFormulas      = predictorFormulas;
            ConversionToFiatPricesState = conversionToFiatPricesState;
            TradeAccountState           = tradeAccountState;
            TradeConnector              = tradeAccountState.Connector;
            this.logger                 = logger;
            Isin                        = isin;
            this.positionFiat           = positionFiat;
            this.fullSideDealShift      = fullSideDealShift;
            this.sumOneSideVolumeFiat   = sumOneSideVolumeFiat;
            this.buyPotentialLimitFiat  = buyPotentialLimitFiat;
            this.sellPotentialLimitFiat = sellPotentialLimitFiat;
            this.isMarginMarket         = isMarginMarket;
            this.lotSize                = lotSize;
            this.isReverse              = isReverse;
            this.tradesWriter           = tradesWriter;
            this.waitPendingTimeoutMs   = waitPendingTimeoutMs;

            dealShift = -1 * positionFiat / sumOneSideVolumeFiat * fullSideDealShift;
            logger.Enqueue($"Initial dealShift={dealShift} using positionFiat={positionFiat} for isin {isin}.");
        }

        public bool IsOrderStatesEmpty(OrderSide side) => side == OrderSide.Buy ? bidStates.Count == 0 : askStates.Count == 0;

        public void SetHedge(AccountState hedgeAccountState, PricesState hedgePricesState, bool shouldStopOnHedgeCancel)
        {
            HedgeAccountState = hedgeAccountState;
            HedgePricesState  = hedgePricesState;
            ShouldHedge       = true;
            ShouldStopOnHedgeCancel = shouldStopOnHedgeCancel;
        }

        public bool IsPotentialOneSideLimitExceeded(OrderSide side, out decimal activeVol)
        {
            decimal activeFiatVolLocal = GetActiveFiatVolume(side);
            activeVol = Math.Abs(activeFiatVolLocal);

            if (side == OrderSide.Buy) return activeFiatVolLocal + positionFiat >= buyPotentialLimitFiat;

            return -1 * activeFiatVolLocal + positionFiat <= -1 * sellPotentialLimitFiat;
        }

        public void TryAddActiveOrder(OrderMessage activeOrder)
        {
            lock (activeOrdersLocker)
            {
                if (allActiveOrdersById.ContainsKey(activeOrder.OrderId)) return;

                UpdateActiveOrdersOneSide(activeOrder);

                allActiveOrdersById.Add(activeOrder.OrderId, activeOrder);
            }
        }

        public void TryUpdateActiveOrders(List<OrderMessage> newActiveOrders)
        {
            lock (activeOrdersLocker)
            {
                foreach (OrderMessage order in newActiveOrders)
                {
                    if (allActiveOrdersById.ContainsKey(order.OrderId)) continue;

                    UpdateActiveOrdersOneSide(order);

                    allActiveOrdersById.Add(order.OrderId, order);
                }
            }
        }

        void UpdateActiveOrdersOneSide(OrderMessage order)
        {
            SortedList<decimal, Dictionary<string, OrderMessage>> oneSideActiveOrders = order.Side == OrderSide.Buy ? activeBids : activeAsks;

            if (!oneSideActiveOrders.TryGetValue(order.Price, out Dictionary<string, OrderMessage> ordersById))
            {
                ordersById                       = new Dictionary<string, OrderMessage>();
                oneSideActiveOrders[order.Price] = ordersById;
            }

            ordersById.Add(order.OrderId, order);
        }

        public void TryRemoveActiveOrder(OrderMessage order)
        {
            lock (activeOrdersLocker)
            {
                if (!allActiveOrdersById.Remove(order.OrderId, out OrderMessage canceledOrder)) return;

                //в переданном сюда order может не быть цены. поэтому берём её из canceledOrder
                RemoveActiveOrdersOneSide(order.Side == OrderSide.Buy ? activeBids : activeAsks, order.OrderId, canceledOrder.Price);
            }
        }

        public bool TryRemoveActiveOrder(string orderId, out OrderMessage canceledOrder)
        {
            lock (activeOrdersLocker)
            {
                if (!allActiveOrdersById.Remove(orderId, out canceledOrder)) return false;

                RemoveActiveOrdersOneSide(canceledOrder.Side == OrderSide.Buy ? activeBids : activeAsks, orderId, canceledOrder.Price);
            }

            return true;
        }

        void RemoveActiveOrdersOneSide(SortedList<decimal, Dictionary<string, OrderMessage>> oneSideActiveOrders, string orderIdToRemove, decimal priceToRemove)
        {
            if (oneSideActiveOrders.TryGetValue(priceToRemove, out Dictionary<string, OrderMessage> orders))
            {
                orders.Remove(orderIdToRemove);
                if (orders.Count == 0) oneSideActiveOrders.Remove(priceToRemove);
            }

            if (oneSideActiveOrders.Count == 0) logger.Enqueue("No active orders left");

            //logger.Enqueue($"Active orders left:\n{string.Join('\n', oneSideActiveOrders.Values.SelectMany(pair => pair.Values))}");
        }

        public void TryApplyExecutionReport(OrderMessage execution)
        {
            lock (activeOrdersLocker)
            {
                if (!allActiveOrdersById.TryGetValue(execution.OrderId, out OrderMessage activeOrder)) return;
                activeOrder.UpdateQty(execution.Qty - execution.TradeQty);
            }
        }

        public void UpdatePosition(OrderMessage execution)
        {
            lock (positionLocker)
            {
                decimal conversionMid = (ConversionToFiatPricesState.Book.BestBid + ConversionToFiatPricesState.Book.BestAsk) / 2;
                int     invertCoef    = execution.Side == OrderSide.Buy ? 1 : -1;
                positionFiat += invertCoef *
                                TradeModelHelpers.FiatVolumeFromQty(isMarginMarket, isReverse, execution.Price, execution.TradeQty, conversionMid, lotSize);
                dealShift = -1 * positionFiat / sumOneSideVolumeFiat * fullSideDealShift;
            }

            logger.Enqueue($"New positionFiat={positionFiat};dealShift={dealShift} for isin {Isin}.");
        }

        public void LogTrade(OrderMessage execution, string exchangeName, string type)
        {
            tradesWriter.WriteLine($"{DateTime.UtcNow:HH:mm:ss};"                                                                                                    +
                                   $"{execution.Timestamp:HH:mm:ss};{execution.OrderId};{execution.Price.ToString(CultureInfo.InvariantCulture).Replace('.', ',')};" +
                                   $"{execution.TradeQty.ToString(CultureInfo.InvariantCulture).Replace('.', ',')};{execution.Side};{execution.TradeFee};"           +
                                   $"{exchangeName};{type}");
        }

        public List<OrderMessage> GetAllActiveOrders()
        {
            List<OrderMessage> allActiveOrders;

            lock (activeOrdersLocker) { allActiveOrders = allActiveOrdersById.Values.ToList(); }

            return allActiveOrders;
        }

        public List<OrderMessage> GetActiveOrdersOneSide(OrderSide side)
        {
            SortedList<decimal, Dictionary<string, OrderMessage>> oneSideActiveOrders = side == OrderSide.Buy ? activeBids : activeAsks;
            List<OrderMessage>                                    oneSideActiveOrdersFlat;

            lock (activeOrdersLocker) { oneSideActiveOrdersFlat = oneSideActiveOrders.Values.SelectMany(level => level.Values).ToList(); }

            return oneSideActiveOrdersFlat;
        }

        public List<OrderMessage> GetActiveOrdersInFrontOfPrice(decimal price, OrderSide side)
        {
            lock (activeOrdersLocker)
            {
                int                                                   index;
                SortedList<decimal, Dictionary<string, OrderMessage>> oneSideActiveOrders;

                if (side == OrderSide.Buy)
                {
                    oneSideActiveOrders = activeBids;

                    //получаем индекс первой заявки, у которой цена больше, чем price
                    index = activeBids.IndexOfNearestGreaterEqualKey(price);
                }
                else
                {
                    oneSideActiveOrders = activeAsks;
                    index               = activeAsks.IndexOfNearestLessEqualKey(price);
                }

                if (index < 0) return null;

                //так как заявка с индексом index перед границей, то взять нужно все заяки до индекса index, то есть всего index + 1 заявок
                List<OrderMessage> orders = oneSideActiveOrders.Values.Take(index + 1).SelectMany(pair => pair.Values).ToList();

                return orders;
            }
        }

        public List<OrderMessage> GetActiveOrdersBehindPrice(decimal price, OrderSide side)
        {
            lock (activeOrdersLocker)
            {
                int                                                   index;
                SortedList<decimal, Dictionary<string, OrderMessage>> oneSideActiveOrders;

                if (side == OrderSide.Buy)
                {
                    oneSideActiveOrders = activeBids;

                    //получаем индекс заявки, у которой цена меньше, чем price
                    index = activeBids.IndexOfNearestLessEqualKey(price);
                }
                else
                {
                    oneSideActiveOrders = activeAsks;
                    index               = activeAsks.IndexOfNearestGreaterEqualKey(price);
                }

                if (index < 0) return null;

                //так как заявка с индексом index уже за границей, то пропускать нужно заявки до индекса index - 1, то есть
                // всего index заявок.
                List<OrderMessage> orders = oneSideActiveOrders.Values.Skip(index).SelectMany(pair => pair.Values).ToList();

                return orders;
            }
        }

        public bool TryGetRandomActiveOrder(OrderSide side, out OrderMessage order)
        {
            lock (activeOrdersLocker)
            {
                order = null;
                SortedList<decimal, Dictionary<string, OrderMessage>> oneSideActivePriceLevels = side == OrderSide.Buy ? activeBids : activeAsks;
                List<OrderMessage> oneSideActiveOrders =
                    oneSideActivePriceLevels.Values.SelectMany(pair => pair.Values).ToList();

                if (oneSideActiveOrders.Count == 0) return false;

                order = oneSideActiveOrders.RandomElement(ThreadSafeRandom.ThisThreadsRandom);
                return true;
            }
        }

        public bool TryGetActivePriceLevelsWithMaxPriceDifference(OrderSide side, out decimal firstPrice, out decimal secondPrice)
        {
            lock (activeOrdersLocker)
            {
                firstPrice  = decimal.MinValue;
                secondPrice = decimal.MinValue;
                SortedList<decimal, Dictionary<string, OrderMessage>> oneSideActivePriceLevels = side == OrderSide.Buy ? activeBids : activeAsks;
                int indexOfFirstKeyWithMaxKeysDifference =
                    oneSideActivePriceLevels.IndexOfFirstKeyWithMaxKeysDifference();

                if (indexOfFirstKeyWithMaxKeysDifference < 0) return false;

                firstPrice  = oneSideActivePriceLevels.Keys[indexOfFirstKeyWithMaxKeysDifference];
                secondPrice = oneSideActivePriceLevels.Keys[indexOfFirstKeyWithMaxKeysDifference + 1];
                return secondPrice > 0 && firstPrice > 0;
            }
        }

        public int GetNumActivePriceLevels(OrderSide side) => side == OrderSide.Buy ? activeBids.Count : activeAsks.Count;

        public decimal GetBestActivePrice(OrderSide side)
        {
            SortedList<decimal, Dictionary<string, OrderMessage>> activeOrders = side == OrderSide.Buy ? activeBids : activeAsks;

            lock (activeOrdersLocker)
            {
                if (activeOrders.Count == 0) return 0;

                return activeOrders.First().Key;
            }
        }

        public decimal GetFarthestActivePrice(OrderSide side)
        {
            SortedList<decimal, Dictionary<string, OrderMessage>> activeOrders       = side == OrderSide.Buy ? activeBids : activeAsks;
            decimal                                               lastUnderlyingBest = side == OrderSide.Buy ? lastUnderlyingBid : lastUnderlyingAsk;

            lock (activeOrdersLocker)
            {
                if (activeOrders.Count == 0)
                {
                    if (lastUnderlyingBest <= 0)
                    {
                        throw new ExecutionFlowException("ActiveOrders dictionary is empty. " +
                                                         $"So intended to return lastUnderlyingPrice={lastUnderlyingBest},  but it is <= 0.");
                    }

                    return lastUnderlyingBest;
                }

                return activeOrders.Last().Key;
            }
        }

        decimal GetActiveFiatVolume(OrderSide side)
        {
            SortedList<decimal, Dictionary<string, OrderMessage>> activeOrders = side == OrderSide.Buy ? activeBids : activeAsks;
            decimal conversionMid =
                (ConversionToFiatPricesState.Book.BestBid + ConversionToFiatPricesState.Book.BestAsk) / 2;

            lock (activeOrdersLocker)
            {
                IEnumerable<OrderMessage> orders = activeOrders.Values.SelectMany(onePriceActiveOrders => onePriceActiveOrders.Values);
                return orders.Sum(order => TradeModelHelpers.FiatVolumeFromQty(isMarginMarket, isReverse, order.Price, order.Qty, conversionMid, lotSize));
            }
        }

        public void SetInitialPredictorsCoefs()
        {
            PredictorsCoefsSet = true;
            decimal baseMid = (baseFormula.MinValue + baseFormula.MaxValue) / 2;

            foreach (FormulaContext predictor in predictorFormulas)
            {
                decimal predictorMid = (predictor.MinValue + predictor.MaxValue) / 2;
                predictor.CoefToBase = predictorMid != 0 ? baseMid / predictorMid : 0;
            }

            logger.Enqueue($"Initial base formula values: min={baseFormula.MinValue};max={baseFormula.MaxValue} for isin {Isin}.");
            logger.Enqueue("Initial predictor formulas values:\n"                                                                                                         +
                           $"{string.Join('\n', predictorFormulas.Select(formula => $"min={formula.MinValue};max={formula.MaxValue};coefToBase={formula.CoefToBase}"))} " +
                           $"for isin {Isin}.");
        }

        public void ShiftPredictorsCoefs(decimal shiftSpeedCoef, bool checkPricesMatch, out bool doPricesMatch)
        {
            decimal baseMid = (baseFormula.MinValue + baseFormula.MaxValue) / 2;

            doPricesMatch = true;
            foreach (FormulaContext predictor in predictorFormulas)
            {
                decimal predictorMid = (predictor.MinValue + predictor.MaxValue) / 2;
                decimal exactCoef    = predictorMid != 0 ? baseMid / predictorMid : 0;
                decimal difference   = exactCoef - predictor.CoefToBase;
                predictor.CoefToBase += difference * shiftSpeedCoef;

                if (checkPricesMatch && exactCoef != 1) doPricesMatch = false;
            }
        }

        public (decimal, decimal, decimal, decimal, decimal) GetPredictorsBidAsk()
        {
            decimal minPredictor = predictorFormulas.Min(formula => formula.MinValue * formula.CoefToBase);
            decimal maxPredictor = predictorFormulas.Max(formula => formula.MaxValue * formula.CoefToBase);
            decimal bidAdjusted  = minPredictor + dealShift;
            decimal askAdjusted  = maxPredictor + dealShift;

            lastUnderlyingBid = bidAdjusted;
            lastUnderlyingAsk = askAdjusted;

            return (bidAdjusted, askAdjusted, minPredictor, maxPredictor, dealShift);
        }

        public decimal GetBaseMid() => (baseFormula.MinValue + baseFormula.MaxValue) / 2 + dealShift;

        public void SetDelayToNextOrdersAction(double delayMuMs, double delaySigma)
        {
            delayToNextOrdersActionMs = (int)ThreadSafeRandom.ThisThreadsRandom.NextGaussian(delayMuMs, delaySigma);
            logger.Enqueue($"Going to wait {delayToNextOrdersActionMs} until next action for isin {Isin}.");
        }

        public void RestartMeasuringDelayToNextAction()
        {
            ordersActionDelayMeasurer.Restart();
        }

        public bool TryGetMarginPosition(string isinP, out decimal position)
        {
            return TradeAccountState.TryGetMarginPosition(isinP, out position);
        }

        public void TryUpdateObligationStateOnActiveOrders(List<OrderMessage> orders)
        {
            foreach (OrderMessage order in orders)
                if (!TryUpdateLocalOrderState(order.OrderId, LocalOrderStatus.Active, order.Side))
                    lostObligationOrders.TryAdd(order.OrderId, order);
        }

        public void TryUpdateObligationStateOnOrderRemoved(OrderMessage order)
        {
            if (!TryUpdateLocalOrderState(order.OrderId, LocalOrderStatus.None, order.Side)) lostObligationOrders.Remove(order.OrderId, out _);
        }

        public void AddOrUpdateLocalOrderState(decimal          obligationSpread,
                                               string           clientOrderId,
                                               LocalOrderStatus status,
                                               OrderSide        side,
                                               decimal          volumeOneSideFiat)
        {
            MultiKeyDictionary<decimal, string, OrderState> orderStates = side == OrderSide.Buy ? bidStates : askStates;

            if (status != LocalOrderStatus.AddPending && orderStates.Count == 0) return;

            if (!orderStates.TryGetValue(obligationSpread, out OrderState orderState))
            {
                if (status == LocalOrderStatus.AddPending)
                {
                    orderState = new OrderState();
                    orderState.InitialSet(clientOrderId, obligationSpread, volumeOneSideFiat, status);
                    orderStates.Add(obligationSpread, clientOrderId, orderState);
                }

                return;
            }

            orderState.SetNewOrderId(clientOrderId, status);
            orderStates.Associate(clientOrderId, obligationSpread);
        }

        public bool TryUpdateLocalOrderState(string clientOrderId, LocalOrderStatus status, OrderSide side)
        {
            MultiKeyDictionary<decimal, string, OrderState> orderStates = side == OrderSide.Buy ? bidStates : askStates;

            if (orderStates.Count == 0 || !orderStates.TryGetValue(clientOrderId, out OrderState orderState)) return false;

            orderState.Update(status);
            return true;
        }

        public List<OrderMessage> GetLostObligationOrders() => lostObligationOrders.Values.ToList();

        //public (List<string> orderIdsToCancel, List<OrderState> obligationsToAdd) GetObligationsToModify(OrderSide side)
        //{
        //    List<string>     orderIdsToCancel = new List<string>();
        //    List<OrderState> obligationsToAdd = new List<OrderState>();

        //    MultiKeyDictionary<decimal, string, OrderState> orderStates = side == OrderSide.Buy ? bidStates : askStates;

        //    foreach (OrderState state in orderStates.Values)
        //    {
        //        if (state.CanCancel(waitPendingTimeoutMs)) orderIdsToCancel.Add(state.CurrentOrderId);
        //        else if (state.CanAddNew(waitPendingTimeoutMs)) obligationsToAdd.Add(state);
        //    }

        //    return (orderIdsToCancel, obligationsToAdd);
        //}

        public List<string> GetObligationsToCancel(OrderSide side)
        {
            var orderIdsToCancel = new List<string>();

            MultiKeyDictionary<decimal, string, OrderState> orderStates = side == OrderSide.Buy ? bidStates : askStates;

            foreach (OrderState state in orderStates.Values)
            {
                if (state.CanCancel(waitPendingTimeoutMs)) orderIdsToCancel.Add(state.CurrentOrderId);
            }

            return orderIdsToCancel;
        }

        public List<OrderState> GetObligationsToAdd(OrderSide side)
        {
            var obligationsToAdd = new List<OrderState>();

            MultiKeyDictionary<decimal, string, OrderState> orderStates = side == OrderSide.Buy ? bidStates : askStates;

            foreach (OrderState state in orderStates.Values)
            {
                if (state.CanAddNew(waitPendingTimeoutMs)) obligationsToAdd.Add(state);
            }

            return obligationsToAdd;
        }
    }
}
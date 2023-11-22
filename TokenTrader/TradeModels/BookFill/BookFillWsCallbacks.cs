using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using SharedDataStructures;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SharedTools;
using SharedTools.Interfaces;
using TokenTrader.OrderBook;
using TokenTrader.State;

namespace TokenTrader.TradeModels.BookFill
{
    partial class BookFill
    {
        void Connector_Connected(object sender, EventArgs e)
        {
            var connector = (IDataConnector)sender;

            if (areDataConnectorsConnected.ContainsKey(connector.Name)) areDataConnectorsConnected[connector.Name] = true;
            else accountStates[connector.Name].IsConnected                                                         = true;

            if (accountStates.Values.All(state => state.IsConnected)              &&
                areDataConnectorsConnected.Values.All(isConnected => isConnected) &&
                !connectedResetEvent.IsSet) connectedResetEvent.Set();

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
                    if (!FillState(connector.ExchangeName, isin, out _, out PricesState state, out _)) continue;

                    string lastBookCrossStartedTimestampString = state.BookBrokenStartedTimestamp.ToString("HH:mm:ss.fff");
                    state.ResetBookBrokenStartedTimestamp();
                    logger.Enqueue($"Reset BookCrossStartedTimestamp={lastBookCrossStartedTimestampString} for isin {isin} for connector {connector.Name}.");
                }
            }

            if (run)
            {
                logger.Enqueue($"Going to restart connector {connector.Name}.");
                connector.Start();
            }
            else { logger.Enqueue($"\"run\" was already set to False, so not trying to start connector {connector.Name}."); }
        }

        void Connector_ActiveOrdersListArrived(object sender, List<OrderMessage> activeOrders)
        {
            var connector          = (IDataConnector)sender;
            var activeOrdersByIsin = new Dictionary<string, List<OrderMessage>>();

            //переписываем все заявки в словарь по исинам. чтобы потом только один раз для каждого исина доставать mmState
            foreach (OrderMessage activeOrder in activeOrders)
            {
                if (!activeOrdersByIsin.TryGetValue(activeOrder.Isin, out List<OrderMessage> singleIsinActiveOrders))
                {
                    singleIsinActiveOrders = new List<OrderMessage>();
                    activeOrdersByIsin.Add(activeOrder.Isin, singleIsinActiveOrders);
                }

                singleIsinActiveOrders.Add(activeOrder);
            }

            //обновляем уже активные заявки
            foreach (KeyValuePair<string, List<OrderMessage>> pair in activeOrdersByIsin)
            {
                string             isin                   = pair.Key;
                List<OrderMessage> singleIsinActiveOrders = pair.Value;

                if (!mmStates.TryGetValue(isin, out IsinMMState state)) continue;

                state.TryUpdateActiveOrders(singleIsinActiveOrders);
                state.TryUpdateObligationStateOnActiveOrders(singleIsinActiveOrders);
            }

            accountStates[connector.Name].SetActiveOrdersEvent();

            string activeOrdersStr = activeOrders.Count > 0 ? $"Got active orders:\n{string.Join('\n', activeOrders)}\n" : "Got no active orders";
            logger.Enqueue($"{activeOrdersStr} for connector {connector.ExchangeName}_{connector.Name}");
        }

        void Connector_NewOrderAdded(object sender, OrderMessage newOrder)
        {
            var connector = (IDataConnector)sender;

            if (!mmStates.TryGetValue(newOrder.Isin, out IsinMMState state)) return;

            state.TryAddActiveOrder(newOrder);
            state.TryUpdateLocalOrderState(newOrder.OrderId, LocalOrderStatus.Active, newOrder.Side);

            logger.Enqueue($"New order: {newOrder} for connector {connector.ExchangeName}_{connector.Name}");
        }

        async void Connector_OrderCanceled(object sender, OrderMessage canceledOrder)
        {
            var connector = (IDataConnector)sender;

            UsedAddOrderPriceData usedPricesData = null;
            if (context.UseUdp) usedPricesDatas.Remove(canceledOrder.OrderId, out usedPricesData);
            usedPricesData = usedPricesData ?? new UsedAddOrderPriceData(0, 0, 0, 0.1m);

            if (mmStates.TryGetValue(canceledOrder.Isin, out IsinMMState state))
            {
                logger.Enqueue($"Canceled Market order: {canceledOrder} {usedPricesData} for connector {connector.ExchangeName}_{connector.Name}");

                state.TryRemoveActiveOrder(canceledOrder);
                state.TryUpdateObligationStateOnOrderRemoved(canceledOrder);

                if (preparedRandomizeOrdersByCancelId.TryRemove(canceledOrder.OrderId, out (decimal price, decimal qty) pair))
                    AddPreparedRandomOrderOnCancel(state, pair.price, pair.qty, canceledOrder);
            }
            else if (mmStatesByHedgeIsin.TryGetValue(canceledOrder.Isin, out state))
            {
                logger.Enqueue($"Canceled Hedge order: {canceledOrder} {usedPricesData} for connector {connector.ExchangeName}_{connector.Name}");

                //если хедж не получился, шлём об этом сообщение через бот
                if (context.UseUdp)
                {
                    string canceledMessage = PrepareUdpTradeMessage(canceledOrder, "CANCELED", "Hedge", usedPricesData);
                    logger.Enqueue($"Going to send {canceledMessage}");
                    udpSender.SendMessage(canceledMessage);
                }

                if (state.ShouldStopOnHedgeCancel) Stop(true);
            }
        }

        void Connector_OrderReplaced(object sender, OrderMessage replacedOrder)
        {
            var connector = (IDataConnector)sender;
            logger.Enqueue($"Replaced order: {replacedOrder} for connector {connector.ExchangeName}_{connector.Name}");
        }

        void Connector_ExecutionReportArrived(object sender, OrderMessage execution)
        {
            var connector = (IDataConnector)sender;

            UsedAddOrderPriceData usedPricesData = null;

            //Console.WriteLine("TRADE!!!");

            //если пришла сделка по обычному торговому исину
            if (mmStates.TryGetValue(execution.Isin, out IsinMMState state) && connector.ExchangeName == state.TradeConnector.ExchangeName)
            {
                if (execution.Qty == execution.TradeQty)
                {
                    if (context.UseUdp) usedPricesDatas.Remove(execution.OrderId, out usedPricesData);
                    state.TryRemoveActiveOrder(execution);
                    state.TryUpdateObligationStateOnOrderRemoved(execution);
                }
                else
                {
                    if (context.UseUdp) usedPricesDatas.TryGetValue(execution.OrderId, out usedPricesData);
                    state.TryApplyExecutionReport(execution);
                    state.TryUpdateLocalOrderState(execution.OrderId, LocalOrderStatus.PartiallyExecuted, execution.Side);
                }

                usedPricesData = usedPricesData ?? new UsedAddOrderPriceData(0, 0, 0, 0.1m);

                state.UpdatePosition(execution);

                SaveAllPositions();

                state.LogTrade(execution, connector.ExchangeName, "Market");
                if (context.UseUdp) udpSender.SendMessage(PrepareUdpTradeMessage(execution, "TRADE", "Market", usedPricesData));

                logger.Enqueue($"Execution report: {execution} {usedPricesData} for connector {connector.ExchangeName}_{connector.Name}");

                if (state.ShouldHedge) tradeHedger.Post(execution);
            }
            //если пришла сделка по хеджевому исину
            else if (mmStatesByHedgeIsin.TryGetValue(execution.Isin, out state) && connector.ExchangeName == state.HedgeAccountState.Connector.ExchangeName)
            {
                state.LogTrade(execution, connector.ExchangeName, "Hedge");
                if (context.UseUdp)
                {
                    if (!usedPricesDatas.Remove(execution.OrderId, out usedPricesData)) usedPricesData = new UsedAddOrderPriceData(0, 0, 0, 0.1m);
                    udpSender.SendMessage(PrepareUdpTradeMessage(execution, "TRADE", "Hedge", usedPricesData));
                }

                usedPricesData = usedPricesData ?? new UsedAddOrderPriceData(0, 0, 0, 0.1m);
                logger.Enqueue($"Hedge execution report: {execution} {usedPricesData} for connector {connector.ExchangeName}_{connector.Name}");
            }
            else logger.Enqueue($"Got an execution for unknown isin: {execution} for connector {connector.ExchangeName}_{connector.Name}");
        }

        void Connector_BalanceArrived(object sender, List<BalanceMessage> balances)
        {
            var connector = (ITradeConnector)sender;

            AccountState accountState = accountStates[connector.Name];

            List<BalanceMessage> usedBalances = balances.Where(balance => usedCurrencies.Contains(balance.Currency)).ToList();

            foreach (BalanceMessage balance in usedBalances)
            {
                accountState.AvailableSpotBalances[balance.Currency] = balance.Available;
                logger.Enqueue($"Balance: {balance} for connector {connector.ExchangeName}_{connector.Name}");
            }

            accountState.SetBalanceEvent();

            if (context.UseUdp)
            {
                string usedBalancesStr = string.Join('\n', usedBalances.Select(balance => $"{balance.Currency}={balance.Available.ToStringNoZeros()}"));
                udpSender.SendMessage($"{context.InstanceName};\nBALANCES;{connector.ExchangeName};\n{usedBalancesStr}");
            }
        }

        void Connector_PositionArrived(object sender, List<PositionMessage> positions)
        {
            var connector = (ITradeConnector)sender;

            AccountState accountState = accountStates[connector.Name];

            var positionsByCurrency = new Dictionary<string, decimal>();

            foreach (PositionMessage position in positions)
                if (positionsByCurrency.ContainsKey(position.Isin)) positionsByCurrency[position.Isin] += position.Qty;
                else positionsByCurrency[position.Isin]                                                =  position.Qty;

            //logger.Enqueue($"Individual position: {position} for connector {connector.ExchangeName}_{connector.Name}");

            foreach (KeyValuePair<string, decimal> pair in positionsByCurrency)
            {
                string  isin     = pair.Key;
                decimal position = pair.Value;
                accountState.UpdateMarginPosition(isin, position);
                logger.Enqueue($"Sum position for isin {isin}={position} for connector {connector.ExchangeName}_{connector.Name}");
            }

            if (context.UseUdp)
            {
                string positionsStr = string.Join('\n',
                                                  positionsByCurrency.Where(pair => pair.Value != 0).
                                                                      Select(pair => $"{pair.Key}={pair.Value.ToStringNoZeros()}"));
                udpSender.SendMessage($"{context.InstanceName};\nPOSITIONS;{connector.ExchangeName};\n{positionsStr}");
            }
        }

        async void HedgeConnector_LimitArrived(object sender, List<LimitMessage> limits)
        {
            var connector = (IHedgeConnector)sender;

            AccountState accountState = accountStates[connector.Name];

            foreach (LimitMessage limit in limits)
            {
                logger.Enqueue($"Limit: {limit} for connector {connector.ExchangeName}_{connector.Name}");
                if (await TryExitOnExposureExceeded(accountState, limit)) return;
            }

            if (context.UseUdp)
            {
                string limitsStr = string.Join('\n', limits);
                udpSender.SendMessage($"{context.InstanceName};\nLIMITS;{connector.ExchangeName};\n{limitsStr}");
            }
        }

        async void Connector_ErrorOccured(object sender, ErrorMessage error)
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

                case (int)RequestError.AddOrder: break;

                case (int)RequestError.CancelOrder:

                    //в качестве description передаётся clientOrderId, чтобы удалить её из списка активных
                    foreach (IsinMMState state in mmStates.Values)
                        if (state.TryRemoveActiveOrder(error.Description, out OrderMessage canceledOrder))
                            state.TryUpdateObligationStateOnOrderRemoved(canceledOrder);

                    preparedRandomizeOrdersByCancelId.TryRemove(error.Description, out _);
                    break;
            }

            if (error.IsCritical)
            {
                string errorDescription = $"Request: {(RequestError)error.Code}\nMessage:\n{error.Message};\nDescription:\n{error.Description}";
                Stop(true, errorDescription);
            }
        }

        void Connector_BookSnapshotArrived(object sender, BookMessage bookMessage)
        {
            ProcessBookMessage(sender, bookMessage, BookHelpers.ApplySnapshot);
        }

        void Connector_BookUpdateArrived(object sender, BookMessage bookMessage)
        {
            ProcessBookMessage(sender, bookMessage, BookHelpers.ApplyUpdate);
        }

        void Connector_TickerArrived(object sender, TickerMessage tickerMessage)
        {
            var connector = (IDataConnector)sender;

            string actualFullIsin = $"{connector.ExchangeName}_{tickerMessage.Isin}";
            if (!context.VariableSubstMap.Reverse.TryGetValue(actualFullIsin, out string variableName)) variableName = actualFullIsin;

            if (variables.TryGetValue(variableName, out VariableData data)) data.PricesState.LastPrice                               = tickerMessage.Last;
            else if (utilityPricesStates.TryGetValue(variableName, out PricesState utilityPricesState)) utilityPricesState.LastPrice = tickerMessage.Last;

            //state.Bid = tickerMessage.Bid;
            //state.Ask = tickerMessage.Ask;
        }

        async Task<bool> TryExitOnExposureExceeded(AccountState accountState, LimitMessage limit)
        {
            if (accountState.IsHedgeExposureLimitOk(limit, out decimal exposureLimitTolerance)) return false;

            string errorDescription = $"Limit {limit} has exceeded boundaries shifted by exposureLimitTolerance={exposureLimitTolerance}";
            Stop(true, errorDescription);

            return true;
        }

        void ProcessBookMessage(object sender, BookMessage bookMessage, Action<BookMessage, UnlimitedOrderBook<long>, int, bool, ILogger> bookApplier)
        {
            if (childSource.IsCancellationRequested) return;

            var connector = (IDataConnector)sender;

            if (!FillState(connector.ExchangeName, bookMessage.Isin, out VariableData variableData, out PricesState state, out bool isVariable)) return;
            UnlimitedOrderBook<long> book = state.Book;

            try { bookApplier(bookMessage, book, NumBookErrorsInWindowToThrow, state.CheckBookCross, logger); }
            catch (OrderBookBrokenException ex)
            {
                Stop(true, ex.MakeString());
                return;
            }

            if (TradeModelHelpers.TryRestartConnectorOnBrokenData(bookMessage,
                                                                  state,
                                                                  connector,
                                                                  logger,
                                                                  tradeDataLocker,
                                                                  childSource,
                                                                  ReconnectOnBrokenBookTimeoutSec,
                                                                  WaitingTimeoutMs)) return;

            //не шлём сообщения, пока всё не проинициализировано
            if (isVariable && run)
            {
                bool arePricesNew = BookHelpers.ArePricesNew(book, true);
                if (TradeModelHelpers.IsStuckLongToStop(state, arePricesNew, $"{connector.ExchangeName}_{bookMessage.Isin}", logger, context.StopOnStuckBookTimeoutSec))
                {
                    Stop(true,
                         $"{bookMessage.Isin} book is stuck with bid={state.Book.BestBid} ask={state.Book.BestAsk} " +
                         $"for at least {context.StopOnStuckBookTimeoutSec} seconds.");
                    return;
                }

                if (!arePricesNew) return;

                variableData.LastUpdatedTimestamp = DateTime.UtcNow;

                //variableMessages.Enqueue(variableData, childSource.Token);
                variableMessageProcessor.Post(variableData);
            }

            if (context.UseUdp && state.IsIntervalSecondsPassed(BalancesRefreshingTimeoutMs))
            {
                state.UpdateLastTimestamp();

                string bookMessageStr = PrepareBookMessage(bookMessage.Isin, book, connector.ExchangeName);
                udpSender.SendMessage(bookMessageStr);
            }
        }

        bool FillState(string exchangeName, string isin, out VariableData variableData, out PricesState state, out bool isVariable)
        {
            state      = null;
            isVariable = false;

            string actualFullIsin = $"{exchangeName}_{isin}";

            if (!context.VariableSubstMap.Reverse.TryGetValue(actualFullIsin, out string variableName)) variableName = actualFullIsin;

            if (variables.TryGetValue(variableName, out variableData))
            {
                state      = variableData.PricesState;
                isVariable = true;
            }
            else if (utilityPricesStates.TryGetValue(variableName, out PricesState utilityPricesState)) { state = utilityPricesState; }
            else { return false; }

            return true;
        }

        string PrepareUdpTradeMessage(OrderMessage trade, string action, string type, UsedAddOrderPriceData usedPricesData)
        {
            return $"{context.InstanceName};{action};{type};{DateTime.UtcNow:HH:mm:ss};{trade.Isin};{trade.Side};"                             +
                   $"{trade.Price.ToString(CultureInfo.InvariantCulture)};{trade.TradeQty.ToString(CultureInfo.InvariantCulture)};"            +
                   $"{usedPricesData.Bid.ToString(CultureInfo.InvariantCulture)};{usedPricesData.Ask.ToString(CultureInfo.InvariantCulture)};" +
                   $"{usedPricesData.DealShift.ToString(CultureInfo.InvariantCulture)}";
        }

        string PrepareBookMessage(string isin, UnlimitedOrderBook<long> book, string exchangeName)
        {
            string bookStr = $"{book.AsksString}\n\n{book.BidsString}";

            return $"{context.InstanceName};BOOK;{exchangeName};{isin};{bookStr}";
        }
    }
}
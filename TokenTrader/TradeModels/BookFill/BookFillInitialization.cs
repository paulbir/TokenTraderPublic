using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Interfaces;
using SharedTools;
using TokenTrader.Initialization;
using TokenTrader.State;

namespace TokenTrader.TradeModels.BookFill
{
    partial class BookFill
    {
        Dictionary<string, AccountState> InitTradableConnectors(IReadOnlyList<ITradeConnector> connectors,
                                                                List<ConnectorSettings>        connectorsSettings,
                                                                List<string>                   isins,
                                                                string                         type)
        {
            if (connectorsSettings.Count != connectors.Count)
                throw new ConfigErrorsException($"ConnectorsSettings array has to contain the same number of elements as {type}" +
                                                "Connectors list for BookFill.");

            var accountStatesByPubKey = new Dictionary<string, AccountState>();

            for (int i = 0; i < connectorsSettings.Count; i++)
            {
                ConnectorSettings connectorSettings = connectorsSettings[i];
                ITradeConnector   connector         = connectors[i];
                string            connectorName     = i == 0 ? $"data_{type}" : $"{type}_only_{i}";

                //только первый коннектор будет получать данные. остальные только торгуют.
                connector.Init(i == 0 ? isins : null, i == 0 ? DataTimeoutSeconds : -1, connectorSettings.PubKey, connectorSettings.SecKey, connectorName);
                var state = new AccountState(connector, logger, childSource);
                accountStates.TryAdd(connectorName, state);
                accountStatesByPubKey.Add(connectorSettings.PubKey, state);
                isinsByConnector.TryAdd(connectorName, isins);
            }

            return accountStatesByPubKey;
        }

        void InitDataConnectors()
        {
            for (int i = 0; i < context.DataConnectorContexts.Count; i++)
            {
                DataConnectorContext dataConnectorContext = context.DataConnectorContexts[i];
                IDataConnector       connector            = dataConnectorContext.Connector;
                var                  dataIsinsToGet       = dataConnectorContext.IsinsToGet.ToList();
                string connectorName = $"data_{connector.ExchangeName}_{i}";
                connector.Init(dataIsinsToGet, DataTimeoutSeconds, "", "", connectorName);
                areDataConnectorsConnected[connector.Name] = false;
                isinsByConnector.TryAdd(connectorName, dataIsinsToGet);
            }
        }

        Dictionary<string, decimal> OpenPositionFile()
        {
            string pathToPositions = Path.Combine("position", "position_usd.csv");

            var posByIsin = new Dictionary<string, decimal>();
            foreach (string line in File.ReadLines(pathToPositions))
            {
                string[] tokens = line.Split(';');
                posByIsin.Add(tokens[0], tokens[1].ToDecimal());
            }

            string pathToPositionsBak = Path.Combine("position", "position_usd.bak");
            File.Copy(pathToPositions, pathToPositionsBak, overwrite: true);

            logger.Enqueue($"Got positionsUSD: {string.Join(';', posByIsin.Select(pair => $"{pair.Key}={pair.Value}"))}.");
            positionsStreamWriter = new StreamWriter(pathToPositions, append: false) {AutoFlush = true};

            return posByIsin;
        }

        void CreateVariablesStates()
        {
            //создаём словарь формул, зависимых от каждой переменной
            var uniqueVariables = new HashSet<string>(context.BaseFormulasByVariable.Keys);
            uniqueVariables.UnionWith(context.PredictorFormulasByVariable.Keys);

            foreach (string variable in uniqueVariables)
            {
                context.BaseFormulasByVariable.TryGetValue(variable, out List<FormulaContext> baseFormulas);
                context.PredictorFormulasByVariable.TryGetValue(variable, out List<FormulaContext> predictorFormulas);

                List<FormulaContext> formulas       = baseFormulas ?? predictorFormulas;
                decimal              defaultValue   = context.NumberOnlyVariables.Contains(variable) ? formulas.First().MinValue : decimal.MinValue;
                bool checkBookCross = !context.NoBookCrossCheckVariables.Contains(variable);
                variables.TryAdd(variable,
                                 new VariableData(baseFormulas,
                                                  predictorFormulas,
                                                  context.MaxSpreadForReadyPricesPerc,
                                                  defaultValue,
                                                  NumBookLevelsToSendUdp,
                                                  BookErrorQueueWindowMs,
                                                  checkBookCross));
            }

            //добавляем в каждую формулу словарь с ценами переменных из этой формулы
            foreach (VariableData variableData in variables.Values)
            {
                foreach (FormulaContext formula in variableData.BaseFormulas) formula.TryStoreVariablesPricesStates(variables);
                foreach (FormulaContext formula in variableData.PredictorFormulas) formula.TryStoreVariablesPricesStates(variables);
            }
        }

        void CreateMMStates(Dictionary<string, AccountState> accountStatesByPubKey, Dictionary<string, decimal> posByIsin)
        {
            Directory.CreateDirectory("trades");
            foreach (KeyValuePair<string, BookFillIsinParams> pair in context.IsinsToTrade)
            {
                string               tradeIsin         = pair.Key;
                BookFillIsinParams   isinToTrade       = pair.Value;
                FormulaContext       baseFormula       = context.BaseFormulaByIsinToTrade[tradeIsin];
                List<FormulaContext> predictorFormulas = context.PredictorFormulasByIsinToTrade[tradeIsin];

                if (!posByIsin.TryGetValue(tradeIsin, out decimal positionFiat))
                    throw new ConfigErrorsException($"Trade isin {tradeIsin} was not found in positions file.");

                string tradeWithPubKey = isinToTrade.TradeWithPubKey;

                string tradesPath       = Path.Combine("trades", $"{tradeIsin.Replace("/", "[slash]")}_{DateTime.UtcNow.Date:dd-MM-yyyy}.csv");
                bool   tradesFileExists = File.Exists(tradesPath);

                var fs           = new FileStream(tradesPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                var tradesWriter = new StreamWriter(fs) {AutoFlush = true};
                if (!tradesFileExists) tradesWriter.WriteLine("LogTimestamp;OrderTimestamp;ClientOrderId;Price;TradeQty;Side;Fee;Exchange;TradeType");

                PricesState conversionPricesState = GetOrAddUtilityPricesState(isinToTrade.ConvertToFiatIsin);

                IsinMMState state = new IsinMMState(baseFormula,
                                                    predictorFormulas,
                                                    conversionPricesState,
                                                    accountStatesByPubKey[tradeWithPubKey],
                                                    logger,
                                                    tradeIsin,
                                                    positionFiat,
                                                    isinToTrade.FullSideDealShiftMinsteps.Value * isinToTrade.MinStep,
                                                    isinToTrade.VolumeOneSideFiat,
                                                    isinToTrade.BuyPotentialLimitFiat,
                                                    isinToTrade.SellPotentialLimitFiat,
                                                    context.IsMarginMarket,
                                                    isinToTrade.LotSize,
                                                    isinToTrade.IsReverse.Value,
                                                    tradesWriter,
                                                    WaitPendingTimeoutMs);

                mmStates.TryAdd(tradeIsin, state);

                if (isinToTrade.UseHedge.Value)
                {
                    AccountState hedgeAccountState = accountStatesByPubKey[isinToTrade.Hedge.HedgeWithPubKey];
                    hedgeAccountState.SetHedge(isinToTrade.Hedge.LimitsToStopOnExposureExceeding);

                    string       fullHedgeIsin     = $"{hedgeAccountState.Connector.ExchangeName}_{isinToTrade.Hedge.HedgeWithIsin}";

                    //если хеджевый исин входит в переменные, то возможно он в переменных он указан через нижнее подчёркивание вместо другого символа.
                    //тогда он должен быть в словаре подстановки для переменных. пробуем достать оттуда это название с подчёркиванием, которое будет использоваться для получения цен.
                    if (context.VariableSubstMap.Reverse.TryGetValue(fullHedgeIsin, out string substIsin) && !string.IsNullOrEmpty(substIsin))
                        fullHedgeIsin = substIsin;

                    state.SetHedge(hedgeAccountState, GetOrAddUtilityPricesState(fullHedgeIsin), isinToTrade.Hedge.StopOnHedgeCancel.Value);
                    mmStatesByHedgeIsin.TryAdd(isinToTrade.Hedge.HedgeWithIsin, state);
                }

                //этот хэшсэт будет фильтровать получаемые лимиты
                usedCurrencies.Add(isinToTrade.BuyMarginCurrency);
                usedCurrencies.Add(isinToTrade.SellMarginCurrency);

                logger.Enqueue($"Created MM state for isin {tradeIsin}");
            }
        }

        PricesState GetOrAddUtilityPricesState(string isin)
        {
            PricesState pricesState;

            //пробуем получить сначала в словаре всех переменных. если нету, то создаём новый, если не создали ранее.
            if (variables.TryGetValue(isin, out VariableData variableData)) pricesState = variableData.PricesState;
            else if (!utilityPricesStates.TryGetValue(isin, out pricesState))
            {
                bool checkBookCross = !context.NoBookCrossCheckVariables.Contains(isin);
                pricesState = new PricesState(context.MaxSpreadForReadyPricesPerc, NumBookLevelsToSendUdp, BookErrorQueueWindowMs, checkBookCross);
                utilityPricesStates.TryAdd(isin, pricesState);
            }

            return pricesState;
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

                //для всех подписываемся на данные. но посылать будут только те, которым положено. просто торговые ничего посылать не будут.
                connector.BookSnapshotArrived += Connector_BookSnapshotArrived;
                connector.BookUpdateArrived   += Connector_BookUpdateArrived;
                connector.TickerArrived       += Connector_TickerArrived;

                if (connector is IHedgeConnector hedgeConnector) hedgeConnector.LimitArrived += HedgeConnector_LimitArrived;
            }

            //var dataTradeConnector = (ITradeConnector)accountStates["data_trade"].Connector;
            //dataTradeConnector.BookSnapshotArrived += Connector_BookSnapshotArrived;
            //dataTradeConnector.BookUpdateArrived += Connector_BookUpdateArrived;
            //dataTradeConnector.TickerArrived += Connector_TickerArrived;

            foreach (DataConnectorContext dataConnectorContext in context.DataConnectorContexts)
            {
                IDataConnector connector = dataConnectorContext.Connector;
                connector.Connected           += Connector_Connected;
                connector.Disconnected        += Connector_Disconnected;
                connector.BookSnapshotArrived += Connector_BookSnapshotArrived;
                connector.BookUpdateArrived   += Connector_BookUpdateArrived;
                connector.TickerArrived       += Connector_TickerArrived;
            }
        }

        Dictionary<string, AccountState> InitTradeAndHedgeConnectors(IEnumerable<ITradeConnector> tradeConnectors, IEnumerable<IHedgeConnector> hedgeConnectors)
        {
            Dictionary<string, AccountState> accountStatesByPubKey =
                InitTradableConnectors(tradeConnectors.ToList(), context.TradeConnectorsSettings, context.TradeConnectorIsins, "trade");

            if (context.IsAnyHedge)
            {
                Dictionary<string, AccountState> hedgeStatesByPubKey =
                    InitTradableConnectors(hedgeConnectors.ToList(), context.HedgeConnectorsSettings, context.HedgeConnectorIsins, "hedge");
                foreach (KeyValuePair<string, AccountState> pair in hedgeStatesByPubKey) accountStatesByPubKey.Add(pair.Key, pair.Value);
            }

            return accountStatesByPubKey;
        }
    }
}
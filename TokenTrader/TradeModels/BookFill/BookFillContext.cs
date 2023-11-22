using System.Collections.Generic;
using System.Linq;
using SharedDataStructures;
using TokenTrader.Initialization;
using TokenTrader.Interfaces;

namespace TokenTrader.TradeModels.BookFill
{
    class BookFillContext : ITradeModelContext
    {
        public List<ConnectorSettings>                  HedgeConnectorsSettings        { get; }
        public Dictionary<string, BookFillIsinParams>   IsinsToTrade                   { get; }
        public List<string>                             TradeConnectorIsins            { get; }
        public List<string>                             HedgeConnectorIsins            { get; }
        public Dictionary<string, List<FormulaContext>> BaseFormulasByVariable         { get; }
        public Dictionary<string, List<FormulaContext>> PredictorFormulasByVariable    { get; }
        public Dictionary<string, FormulaContext>       BaseFormulaByIsinToTrade       { get; }
        public Dictionary<string, List<FormulaContext>> PredictorFormulasByIsinToTrade { get; }
        public HashSet<string>                          NumberOnlyVariables            { get; }
        public Dictionary<string, int>                  NextActionDelayByVariable      { get; }
        public ConcurrentMap<string, string>            VariableSubstMap               { get; }
        public bool                                     UseUdp                         { get; }
        public int                                      UDPListenPort                  { get; }
        public int                                      UDPSendPort                    { get; }
        public bool                                     CheckPricesMatch               { get; }
        public string                                   InstanceName                   { get; }

        public List<ConnectorSettings>                            TradeConnectorsSettings         { get; }
        public List<DataConnectorContext>                         DataConnectorContexts           { get; }
        public Dictionary<string, (string isin, string exchange)> ConversionToFiatIsinByTradeIsin { get; }
        public decimal                                            MaxSpreadForReadyPricesPerc     { get; }
        public bool                                               IsMarginMarket                  { get; }
        public HashSet<string>                                    NoBookCrossCheckVariables       { get; }
        public int                                                StopOnStuckBookTimeoutSec       { get; }

        public bool                                               IsAnyRandomFillIsin             => IsinsToTrade.Values.Any(isinData => isinData.MarketMakingModel == MarketMakingModels.RandomFill);
        public bool                                               IsAnyHedge                      => IsinsToTrade.Values.Any(isinData => isinData.UseHedge.Value);

        public int                                                MinOrdersNextActionDelayMuMs    => IsinsToTrade.Values.Min(isinData => isinData.OrdersNextActionDelayMuMs);

        public BookFillContext(List<ConnectorSettings>                            tradeConnectorsSettings,
                               List<ConnectorSettings>                            hedgeConnectorsSettings,
                               List<DataConnectorContext>                         dataConnectorContexts,
                               Dictionary<string, (string isin, string exchange)> conversionToFiatIsinByTradeIsin,
                               Dictionary<string, BookFillIsinParams>             isinsToTrade,
                               List<string>                                       tradeConnectorIsins,
                               List<string>                                       hedgeConnectorIsins,
                               Dictionary<string, List<FormulaContext>>           baseFormulasByVariable,
                               Dictionary<string, List<FormulaContext>>           predictorFormulasByVariable,
                               Dictionary<string, FormulaContext>                 baseFormulaByIsinToTrade,
                               Dictionary<string, List<FormulaContext>>           predictorFormulasByIsinToTrade,
                               HashSet<string>                                    numberOnlyVariables,
                               Dictionary<string, int>                            nextActionDelayByVariable,
                               ConcurrentMap<string, string>                      variableSubstMap,
                               decimal                                            maxSpreadForReadyPricesPerc,
                               bool                                               isMarginMarket,
                               bool                                               useUdp,
                               int                                                udpListenPort,
                               int                                                udpSendPort,
                               bool                                               checkPricesMatch,
                               string                                             instanceName,
                               List<string>                                       noBookCrossCheckVariables,
                               int                                                stopOnStuckBookTimeoutSec)
        {
            TradeConnectorsSettings         = tradeConnectorsSettings;
            HedgeConnectorsSettings         = hedgeConnectorsSettings;
            DataConnectorContexts           = dataConnectorContexts;
            ConversionToFiatIsinByTradeIsin = conversionToFiatIsinByTradeIsin;
            IsinsToTrade                    = isinsToTrade;
            TradeConnectorIsins             = tradeConnectorIsins;
            HedgeConnectorIsins             = hedgeConnectorIsins;
            BaseFormulasByVariable          = baseFormulasByVariable;
            PredictorFormulasByVariable     = predictorFormulasByVariable;
            BaseFormulaByIsinToTrade        = baseFormulaByIsinToTrade;
            PredictorFormulasByIsinToTrade  = predictorFormulasByIsinToTrade;
            NumberOnlyVariables             = numberOnlyVariables;
            NextActionDelayByVariable       = nextActionDelayByVariable;
            VariableSubstMap                = variableSubstMap;
            MaxSpreadForReadyPricesPerc     = maxSpreadForReadyPricesPerc;
            IsMarginMarket                  = isMarginMarket;
            UseUdp                          = useUdp;
            UDPListenPort                   = udpListenPort;
            UDPSendPort                     = udpSendPort;
            CheckPricesMatch                = checkPricesMatch;
            InstanceName                    = instanceName;
            StopOnStuckBookTimeoutSec  = stopOnStuckBookTimeoutSec;
            NoBookCrossCheckVariables       = new HashSet<string>(noBookCrossCheckVariables);
        }
    }
}
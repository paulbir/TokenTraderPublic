using System.Collections.Generic;
using TokenTrader.Initialization;
using TokenTrader.Interfaces;

namespace TokenTrader.TradeModels
{
    class SimultaneousTradesContext : ITradeModelContext
    {
        public Dictionary<string, SimultaneousTradesIsinParams> IsinsToTrade                           { get; }
        public List<string>                                     AdditionalIsinsToGetFromTradeConnector { get; }
        public decimal                                          MaxSpreadToChooseMidPricePerc          { get; }
        public int                                              MinBasePriceOffsetFromBestMinsteps     { get; }
        public int                                              MaxTradeDelaySec                       { get; }
        public bool                                             HoldBuyOrder                           { get; }
        public bool                                             HoldSellOrder                          { get; }
        public bool                                             SafeBuySideMode                        { get; }
        public bool                                             UseVWAP                                { get; }
        public bool                                             EnableSlowDelayMode                    { get; }
        public bool                                             EnableHighVolumeMode                   { get; }
        public decimal                                          BaseQtySigmaFrac                       { get; }
        public bool                                             SlowDownOnNarrowSpread                 { get; }

        public List<ConnectorSettings>                            TradeConnectorsSettings         { get; }
        public List<DataConnectorContext>                         DataConnectorContexts           { get; }
        public Dictionary<string, (string isin, string exchange)> ConversionToFiatIsinByTradeIsin { get; }
        public decimal                                            MaxSpreadForReadyPricesPerc     { get; }
        public bool                                               IsMarginMarket                  { get; }
        public HashSet<string>                                    NoBookCrossCheckVariables       { get; }
        public int                                                StopOnStuckBookTimeoutSec       { get; }

        public SimultaneousTradesContext(List<ConnectorSettings>                            tradeConnectorsSettings,
                                         List<DataConnectorContext>                         dataConnectorContexts,
                                         Dictionary<string, (string isin, string exchange)> conversionToFiatIsinByTradeIsin,
                                         Dictionary<string, SimultaneousTradesIsinParams>   isinsToTrade,
                                         List<string>                                       additionalIsinsToGetFromTradeConnector,
                                         decimal                                            maxSpreadToChooseMidPricePerc,
                                         int                                                minLastDistanceFromBestMinsteps,
                                         int                                                maxTradeDelaySec,
                                         bool                                               holdBuyOrder,
                                         bool                                               holdSellOrder,
                                         bool                                               safeBuySideMode,
                                         bool                                               useVWAP,
                                         bool                                               enableSlowDelayMode,
                                         bool                                               enableHighVolumeMode,
                                         decimal                                            baseQtySigmaFrac,
                                         bool                                               slowDownOnNarrowSpread,
                                         decimal                                            maxSpreadForReadyPricesPerc,
                                         bool                                               isMarginMarket,
                                         List<string>                                       noBookCrossCheckVariables,
                                         int                                                stopOnStuckBookTimeoutSec)
        {
            TradeConnectorsSettings                = tradeConnectorsSettings;
            DataConnectorContexts                  = dataConnectorContexts;
            ConversionToFiatIsinByTradeIsin        = conversionToFiatIsinByTradeIsin;
            IsinsToTrade                           = isinsToTrade;
            AdditionalIsinsToGetFromTradeConnector = additionalIsinsToGetFromTradeConnector;
            MaxSpreadToChooseMidPricePerc          = maxSpreadToChooseMidPricePerc;
            MinBasePriceOffsetFromBestMinsteps     = minLastDistanceFromBestMinsteps;
            MaxTradeDelaySec                       = maxTradeDelaySec;
            HoldBuyOrder                           = holdBuyOrder;
            HoldSellOrder                          = holdSellOrder;
            SafeBuySideMode                        = safeBuySideMode;
            UseVWAP                                = useVWAP;
            EnableSlowDelayMode                    = enableSlowDelayMode;
            EnableHighVolumeMode                   = enableHighVolumeMode;
            BaseQtySigmaFrac                       = baseQtySigmaFrac;
            SlowDownOnNarrowSpread                 = slowDownOnNarrowSpread;
            MaxSpreadForReadyPricesPerc            = maxSpreadForReadyPricesPerc;
            IsMarginMarket                         = isMarginMarket;
            StopOnStuckBookTimeoutSec         = stopOnStuckBookTimeoutSec;
            NoBookCrossCheckVariables              = new HashSet<string>(noBookCrossCheckVariables);
        }
    }
}
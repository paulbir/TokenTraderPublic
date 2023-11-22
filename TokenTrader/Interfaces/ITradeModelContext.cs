using System.Collections.Generic;
using TokenTrader.Initialization;

namespace TokenTrader.Interfaces
{
    interface ITradeModelContext
    {
        List<ConnectorSettings>                            TradeConnectorsSettings         { get; }
        List<DataConnectorContext>                         DataConnectorContexts           { get; }
        Dictionary<string, (string isin, string exchange)> ConversionToFiatIsinByTradeIsin { get; }
        decimal                                            MaxSpreadForReadyPricesPerc     { get; }
        bool                                               IsMarginMarket                  { get; }
        HashSet<string>                                    NoBookCrossCheckVariables       { get; }
        int                                                StopOnStuckBookTimeoutSec       { get; }
    }
}
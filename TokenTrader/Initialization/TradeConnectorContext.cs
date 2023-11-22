using SharedDataStructures.Interfaces;

namespace TokenTrader.Initialization
{
    class TradeConnectorContext : ITradeConnectorContext
    {
        public bool IsMarginMarket { get; }

        public TradeConnectorContext(bool isMarginMarket)
        {
            IsMarginMarket = isMarginMarket;
        }
    }
}
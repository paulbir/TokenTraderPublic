using SharedDataStructures.Messages;

namespace DeribitConnector.Model
{
    class DeribitTickerMessage : TickerMessage
    {
        public DeribitTickerMessage(string instrument_name, decimal? best_bid_price, decimal? best_ask_price, decimal? last_price)
        {
            Isin = instrument_name;
            Bid  = best_bid_price ?? 0;
            Ask  = best_ask_price ?? 0;
            Last = last_price ?? 0;
        }
    }
}
using SharedDataStructures.Messages;

namespace CREXConnector.Model
{
    class CREXTickerMessage : TickerMessage
    {
        public CREXTickerMessage(string instrument, decimal best_bid_price, decimal best_ask_price, decimal last_trade_price)
        {
            Isin = instrument;
            Bid = best_bid_price;
            Ask = best_ask_price;
            Last = last_trade_price;
        }
    }
}
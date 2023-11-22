using SharedDataStructures.Messages;
using SharedTools;

namespace HitBTCConnector.Model
{
    class HitBTCTickerMessage : TickerMessage
    {
        public HitBTCTickerMessage(string ask, string bid, string last, string symbol)
        {
            Isin = symbol;
            Bid = bid.ToDecimal();
            Ask = ask.ToDecimal();
            Last = last.ToDecimal();
        }
    }
}
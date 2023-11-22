using SharedDataStructures.Messages;

namespace GlobitexConnector.Model
{
    class GlobitexTickerMessage : TickerMessage
    {
        public GlobitexTickerMessage(decimal bid, decimal ask, decimal last)
        {
            Bid = bid;
            Ask = ask;
            Last = last;
        }
    }
}
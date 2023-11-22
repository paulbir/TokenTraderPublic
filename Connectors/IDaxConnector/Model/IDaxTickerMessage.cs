using SharedDataStructures.Messages;

namespace IDaxConnector.Model
{
    // ReSharper disable once InconsistentNaming
    class IDaxTickerMessage : TickerMessage
    {
        public IDaxTickerMessage(string isin, decimal bid, decimal ask, decimal last)
        {
            Isin = isin;
            Bid = bid;
            Ask = ask;
            Last = last;
        }
    }
}
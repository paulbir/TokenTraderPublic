using SharedDataStructures.Messages;

namespace XenaConnector.Model
{
    class XenaTickerMessage : TickerMessage
    {
        public XenaTickerMessage(string isin, decimal bid, decimal ask, decimal last)
        {
            Isin = isin;
            Bid = bid;
            Ask = ask;
            Last = last;
        }
    }
}
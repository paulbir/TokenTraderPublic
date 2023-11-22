using SharedDataStructures.Messages;

namespace DutyFlyConnector.Model
{
    class DutyFlyTickerMessage : TickerMessage
    {
        public DutyFlyTickerMessage(string isin, decimal bid, decimal ask, decimal last)
        {
            Isin = isin;
            Bid = bid;
            Ask = ask;
            Last = last;
        }
    }
}
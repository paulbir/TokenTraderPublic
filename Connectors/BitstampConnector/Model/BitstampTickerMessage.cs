using SharedDataStructures.Messages;

namespace BitstampConnector.Model
{
    class BitstampTickerMessage : TickerMessage
    {
        bool areBestsSet;
        bool isLastSetSet;

        public bool IsSet => areBestsSet && isLastSetSet;

        public BitstampTickerMessage(string isin)
        {
            Isin = isin;
        }

        public BitstampTickerMessage(string isin, decimal bid, decimal ask, decimal last)
        {
            Isin = isin;
            Bid  = bid;
            Ask  = ask;
            Last = last;

            areBestsSet  = true;
            isLastSetSet = true;
        }

        public void SetBests(decimal bid, decimal ask)
        {
            Bid = bid;
            Ask = ask;

            areBestsSet = true;
        }

        public void SetLast(decimal last)
        {
            Last = last;

            isLastSetSet = true;
        }

        public BitstampTickerMessage CreateDeepCopy()
        {
            return new BitstampTickerMessage(Isin, Bid, Ask, Last);
        }
    }
}
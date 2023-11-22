using System.Collections.Generic;
using SharedDataStructures.Messages;

namespace BitstampConnector.Model
{
    class BitstampBookMessage : BookMessage
    {
        public decimal BestBid => Bids[0].Price;
        public decimal BestAsk => Asks[0].Price;

        public BitstampBookMessage(List<List<decimal>> bids, List<List<decimal>> asks, long timestamp)
        {
            Sequence = timestamp;
            Bids     = new List<PriceLevel>();
            Asks     = new List<PriceLevel>();

            foreach (List<decimal> level in bids) Bids.Add(new BitstampPriceLevel(level[0], level[1]));
            foreach (List<decimal> level in asks) Asks.Add(new BitstampPriceLevel(level[0], level[1]));
        }

        public void SetIsin(string isin)
        {
            Isin = isin;
        }
    }
}
using System.Collections.Generic;
using SharedDataStructures.Messages;

namespace DummyConnector.Model
{
    class DummyBookMessage : BookMessage
    {
        public DummyBookMessage(decimal mid, string isin, long sequence)
        {
            Isin = isin;
            Sequence = sequence;
            decimal bid = mid - 0.001m;
            decimal ask = mid + 0.001m;

            Bids = new List<PriceLevel>{new DummyPriceLevel(bid, decimal.MaxValue)};
            Asks = new List<PriceLevel>{new DummyPriceLevel(ask, decimal.MaxValue)};
        }
    }
}

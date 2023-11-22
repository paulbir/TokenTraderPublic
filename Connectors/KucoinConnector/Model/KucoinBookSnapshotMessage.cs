using System.Collections.Generic;
using System.Linq;
using SharedDataStructures.Messages;

namespace KucoinConnector.Model
{
    class KucoinBookSnapshotMessage : BookMessage
    {
        public KucoinBookSnapshotMessage(long sequence, List<List<decimal>> bids, List<List<decimal>> asks)
        {
            Sequence = sequence;
            Bids = bids.Select(level => (PriceLevel)new KucoinPriceLevel(level[0], level[1])).ToList();
            Asks = asks.Select(level => (PriceLevel)new KucoinPriceLevel(level[0], level[1])).ToList();
        }

        public void SetIsin(string isin)
        {
            Isin = isin;
        }
    }
}

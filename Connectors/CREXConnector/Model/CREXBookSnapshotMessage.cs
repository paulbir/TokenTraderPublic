using System.Collections.Generic;
using System.Linq;
using SharedDataStructures.Messages;

namespace CREXConnector.Model
{
    class CREXBookSnapshotMessage : BookMessage
    {
        public CREXBookSnapshotMessage(string instrument, List<CREXPriceLevel> bids, List<CREXPriceLevel> asks)
        {
            Isin = instrument;
            Sequence = 0;
            Bids = bids == null ? new List<PriceLevel>() : bids.Select(bid => (PriceLevel)bid).ToList();
            Asks = asks == null ? new List<PriceLevel>() : asks.Select(ask => (PriceLevel)ask).ToList();
        }
    }
}
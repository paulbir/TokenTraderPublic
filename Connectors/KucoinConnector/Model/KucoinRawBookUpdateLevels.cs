using System.Collections.Generic;
using System.Linq;
using SharedDataStructures.Messages;

namespace KucoinConnector.Model
{
    class KucoinRawBookUpdateLevels
    {
        public List<PriceLevel> Bids { get; }
        public List<PriceLevel> Asks { get; }

        public KucoinRawBookUpdateLevels(List<List<decimal>> bids, List<List<decimal>> asks)
        {
            Bids = bids.Select(level => (PriceLevel)new KucoinPriceLevel(level[0], level[1])).ToList();
            Asks = asks.Select(level => (PriceLevel)new KucoinPriceLevel(level[0], level[1])).ToList();
        }
    }
}
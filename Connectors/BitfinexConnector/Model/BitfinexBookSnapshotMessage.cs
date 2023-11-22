using System.Collections.Generic;
using System.Linq;
using SharedDataStructures.Messages;

namespace BitfinexConnector.Model
{
    class BitfinexBookSnapshotMessage : BookMessage
    {
        public BitfinexBookSnapshotMessage(string isin, List<BitfinexPriceLevel> priceLevels)
        {
            Isin = isin;
            Sequence = 0;
            Bids = priceLevels.Where(level => level.Side == OrderSide.Buy).Select(level => (PriceLevel)level).ToList();
            Asks = priceLevels.Where(level => level.Side == OrderSide.Sell).Select(level => (PriceLevel)level).ToList();
        }
    }
}
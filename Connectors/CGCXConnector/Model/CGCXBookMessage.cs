using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using SharedDataStructures.Messages;

namespace CGCXConnector.Model
{
    class CGCXBookMessage : BookMessage
    {
        public int IsinId { get; }
        public decimal BestBid { get; }
        public decimal BestAsk { get; }
        public decimal Last { get; }

        public CGCXBookMessage(List<List<decimal>> rawPriceLevels)
        {
            if (rawPriceLevels == null || rawPriceLevels.Count == 0)
            {
                Bids = new List<PriceLevel>();
                Asks = new List<PriceLevel>();
                return;
            }

            List<CGCXPriceLevel> priceLevels = rawPriceLevels.Select(rawPriceLevel => new CGCXPriceLevel(rawPriceLevel)).ToList();

            Sequence = priceLevels[priceLevels.Count - 1].SequenceNumber;
            IsinId = priceLevels[0].IsinId;

            Bids = priceLevels.Where(priceLevel => priceLevel.Price > 0 && priceLevel.Side == OrderSide.Buy).Select(priceLevel => (PriceLevel)priceLevel).
                               ToList();

            Asks = priceLevels.Where(priceLevel => priceLevel.Price > 0 && priceLevel.Side == OrderSide.Sell).Select(priceLevel => (PriceLevel)priceLevel).
                               ToList();

            BestBid = Bids.Count > 0 ? Bids[0].Price : 0;
            BestAsk = Asks.Count > 0 ? Asks[0].Price : 0;
            Last = priceLevels.Count > 0 ? priceLevels[0].Last : 0;
        }

        public void SetIsin(string isin)
        {
            Isin = isin;
        }
    }
}
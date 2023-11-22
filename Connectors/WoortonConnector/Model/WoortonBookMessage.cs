using System.Collections.Generic;
using SharedDataStructures.Messages;
using SharedTools;

namespace WoortonConnector.Model
{
    class WoortonBookMessage : BookMessage
    {
        static readonly IComparer<decimal> BidComparer = new ReverseComparer<decimal>();
        static readonly IComparer<decimal> AskComparer = Comparer<decimal>.Default;

        public WoortonBookMessage(string instrument, RawLevels levels, long timestamp)
        {
            Isin     = instrument;
            Sequence = timestamp;

            Bids = TransformToNormalLevels(levels.RawBids, BidComparer);
            Asks = TransformToNormalLevels(levels.RawAsks, AskComparer);
        }

        List<PriceLevel> TransformToNormalLevels(List<WoortonPriceLevel> levels, IComparer<decimal> comparer)
        {
            if (levels == null) return new List<PriceLevel>();

            var sortedLevels = new SortedDictionary<decimal, PriceLevel>(comparer);
            foreach (WoortonPriceLevel level in levels)
            {
                if (sortedLevels.TryGetValue(level.Price, out PriceLevel storedLevel)) ((WoortonPriceLevel)storedLevel).IncreaseQty(level.Qty);
                else sortedLevels.Add(level.Price, new WoortonPriceLevel(level.Price, level.Qty));
            }

            return new List<PriceLevel>(sortedLevels.Values);
        }
    }
}
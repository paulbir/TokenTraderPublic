using System.Collections.Generic;
using System.Linq;
using SharedDataStructures.Messages;

namespace GlobitexConnector.Model
{
    class GlobitexBookMessage : BookMessage
    {
        public decimal BestBid { get; }
        public decimal BestAsk { get; }
        public decimal Last { get; }
    
        public GlobitexBookMessage(string symbol, long snapshotSeqNo, List<GlobitexPriceLevel> bid, List<GlobitexPriceLevel> ask, List<Trade> trade)
        {
            Isin = symbol;
            Sequence = snapshotSeqNo;
            Bids = bid.Select(priceLevel => (PriceLevel)priceLevel).ToList();
            Asks = ask.Select(priceLevel => (PriceLevel)priceLevel).ToList();

            BestBid = Bids?.Count > 0 ? Bids[0].Price : 0;
            BestAsk = Asks?.Count > 0 ? Asks[0].Price : 0;

            if (trade == null) return;
            Last = trade.Count > 0 ? trade.Aggregate((t1, t2) => t1.Timestamp > t2.Timestamp ? t1 : t2).Price : 0;
        }
    }
}
using System.Collections.Generic;
using System.Linq;
using SharedDataStructures.Messages;

namespace HitBTCConnector.Model
{
    class HitBTCBookMessage : BookMessage
    {
        public HitBTCBookMessage(IEnumerable<HitBTCPriceLevel> ask, IEnumerable<HitBTCPriceLevel> bid, long sequence, string symbol)
        {
            Isin = symbol;
            Sequence = sequence;
            Bids = bid.Select(pricelevel => (PriceLevel)pricelevel).ToList();
            Asks = ask.Select(pricelevel => (PriceLevel)pricelevel).ToList();
        }
    }
}
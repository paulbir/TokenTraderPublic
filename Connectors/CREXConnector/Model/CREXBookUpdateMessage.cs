using System.Collections.Generic;
using SharedDataStructures.Messages;

namespace CREXConnector.Model
{
    class CREXBookUpdateMessage : BookMessage
    {
        public CREXBookUpdateMessage(long id, string instrument, string direction, decimal price, decimal size)
        {
            Isin = instrument;
            Sequence = id;
            if (direction == "BUY")
            {
                Bids = new List<PriceLevel> {new CREXPriceLevel(price, size)};
                Asks = new List<PriceLevel>();
            }
            else
            {
                Bids = new List<PriceLevel>();
                Asks = new List<PriceLevel> {new CREXPriceLevel(price, size)};
            }
        }
    }
}
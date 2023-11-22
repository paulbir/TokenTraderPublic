using System;
using System.Collections.Generic;
using SharedDataStructures.Messages;

namespace OceanConnector.Model
{
    class OceanBookMessage : BookMessage
    {
        public OceanBookMessage(string isin, decimal bid, decimal ask)
        {
            Isin     = isin;
            Sequence = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Bids     = new List<PriceLevel> {new OceanPriceLevel(bid)};
            Asks     = new List<PriceLevel> {new OceanPriceLevel(ask)};
        }
    }
}
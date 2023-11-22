using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SharedDataStructures.Messages;

namespace BinanceConnector.Model
{
    public class BinanceBookSnapshotMessage : BookMessage
    {
        public BinanceBookSnapshotMessage(long lastUpdateId, List<List<object>> bids, List<List<object>> asks)
        {
            Sequence = lastUpdateId;

            Bids = bids.Select(bid => (PriceLevel)new BinancePriceLevel(Convert.ToDecimal(bid[0], CultureInfo.InvariantCulture),
                                                                        Convert.ToDecimal(bid[1], CultureInfo.InvariantCulture))).ToList();
            Asks = asks.Select(ask => (PriceLevel)new BinancePriceLevel(Convert.ToDecimal(ask[0], CultureInfo.InvariantCulture),
                                                                        Convert.ToDecimal(ask[1], CultureInfo.InvariantCulture))).ToList();
        }

        public void SetIsin(string isin)
        {
            Isin = isin;
        }
    }
}
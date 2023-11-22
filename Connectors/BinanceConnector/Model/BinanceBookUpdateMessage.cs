using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SharedDataStructures.Messages;

namespace BinanceConnector.Model
{
    public class BinanceBookUpdateMessage : BookMessage
    {
        public BinanceBookUpdateMessage(string s, long u, List<List<object>> b, List<List<object>> a)
        {
            Isin = s.ToLowerInvariant() + ".depth";
            Sequence = u;

            Bids = b.Select(bid => (PriceLevel)new BinancePriceLevel(Convert.ToDecimal(bid[0], CultureInfo.InvariantCulture),
                                                                     Convert.ToDecimal(bid[1], CultureInfo.InvariantCulture))).ToList();
            Asks = a.Select(ask => (PriceLevel)new BinancePriceLevel(Convert.ToDecimal(ask[0], CultureInfo.InvariantCulture),
                                                                     Convert.ToDecimal(ask[1], CultureInfo.InvariantCulture))).ToList();
        }
    }
}
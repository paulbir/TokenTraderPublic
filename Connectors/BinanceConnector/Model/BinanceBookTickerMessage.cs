using System.Collections.Generic;
using SharedDataStructures.Messages;

namespace BinanceConnector.Model
{
    class BinanceBookTickerMessage : BookMessage
    {
        public BinanceBookTickerMessage(long u, string s, decimal b, decimal B, decimal a, decimal A)
        {
            Isin = s.ToLowerInvariant() + ".bookTicker";
            Sequence = u;

            Bids = new List<PriceLevel> {new BinancePriceLevel(b, B)};
            Asks = new List<PriceLevel> {new BinancePriceLevel(a, A)};
        }
    }
}
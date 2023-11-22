using System;
using System.Collections.Generic;
using System.Linq;
using SharedDataStructures.Messages;

namespace BitMEXConnector.Model
{
    public class BitMEXBookMessage : BookMessage
    {
        public BitMEXBookMessage(List<BitMEXPriceLevel> levels)
        {
            Isin = levels[0].Isin;
            Bids = levels.Where(level => level.Side == "Buy").Select(level => (PriceLevel)level).ToList();
            Asks = levels.Where(level => level.Side == "Sell").Select(level => (PriceLevel)level).ToList();
            Sequence = DateTime.UtcNow.Ticks;
        }
    }
}
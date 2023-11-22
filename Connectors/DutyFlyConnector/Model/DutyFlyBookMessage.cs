using System.Collections.Generic;
using System.Linq;
using SharedDataStructures.Messages;

namespace DutyFlyConnector.Model
{
    class DutyFlyBookMessage : BookMessage
    {
        public decimal BestBid => Bids.Count == 0 ? 0 : Bids[0].Price;
        public decimal BestAsk => Asks.Count == 0 ? 0 : Asks[0].Price;

        public DutyFlyBookMessage(List<DutyFlyPriceLevel> buy, List<DutyFlyPriceLevel> sell, string symbol)
        {
            Isin = symbol;
            Bids = buy == null ? new List<PriceLevel>() : buy.Select(level => (PriceLevel)level).ToList();
            Asks = sell == null ? new List<PriceLevel>() : sell.Select(level => (PriceLevel)level).ToList();
        }
    }
}
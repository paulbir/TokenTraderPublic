using System.Linq;
using SharedDataStructures.Messages;
using TmexConnector.Model.Public.Data;
using TmexConnector.Model.Shared;

namespace TmexConnector
{
    class TmexBookMessage : BookMessage
    {
        public TmexBookMessage(string isin, long sequence, OrderBookLevel[] levels)
        {
            Isin = isin;
            Sequence = sequence;
            Bids = levels.Where(level => level.Side == TradeSide.Buy).Select(level => (PriceLevel)level).ToList();
            Asks = levels.Where(level => level.Side == TradeSide.Sell).Select(level => (PriceLevel)level).ToList();
        }
    }
}
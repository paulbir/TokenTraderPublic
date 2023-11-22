using System.Linq;
using SharedDataStructures.Messages;

namespace WoortonV2Connector.Model
{
    class WoortonV2BookMessage : BookMessage
    {
        public WoortonV2BookMessage(string instrument, RawPriceLevels levels, long timestamp)
        {
            Isin     = instrument;
            Sequence = timestamp;
            Bids     = levels.Bids.Select(level => (PriceLevel)level).ToList();
            Asks     = levels.Asks.Select(level => (PriceLevel)level).ToList();
        }

        //public void SetIsin(string isin)
        //{
        //    Isin = isin;
        //}
    }
}
using SharedDataStructures;
using SharedDataStructures.Messages;

namespace WoortonV2Connector.Model
{
    class WoortonV2PriceLevel : PriceLevel
    {
        public WoortonV2PriceLevel(decimal price, decimal quantity)
        {
            Price       = price;
            Qty         = quantity;
            ApplyMethod = PriceLevelApplyMethod.OrderLog;
        }
    }
}
using SharedDataStructures;
using SharedDataStructures.Messages;

namespace FineryConnector.Model
{
    class FineryPriceLevel : PriceLevel
    {
        public FineryPriceLevel(decimal price, decimal qty, PriceLevelApplyMethod applyMethod)
        {
            Price       = price;
            Qty         = qty;
            ApplyMethod = applyMethod;
        }
    }
}
using SharedDataStructures.Messages;

namespace DeribitConnector.Model
{
    public class DeribitPriceLevel : PriceLevel
    {
        public DeribitPriceLevel(decimal price, decimal qty)
        {
            Price = price;
            Qty = qty;
        }
    }
}
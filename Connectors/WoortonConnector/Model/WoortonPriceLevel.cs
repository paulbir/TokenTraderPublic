using SharedDataStructures.Messages;

namespace WoortonConnector.Model
{
    class WoortonPriceLevel : PriceLevel
    {
        public WoortonPriceLevel(decimal price, decimal quantity)
        {
            Price = price;
            Qty   = quantity;
        }

        public void IncreaseQty(decimal qty)
        {
            Qty += qty;
        }
    }
}
using SharedDataStructures.Messages;

namespace BitstampConnector.Model
{
    class BitstampPriceLevel : PriceLevel
    {
        public BitstampPriceLevel(decimal price, decimal qty)
        {
            Price = price;
            Qty   = qty;
        }
    }
}
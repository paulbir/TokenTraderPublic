using SharedDataStructures.Messages;

namespace KucoinConnector.Model
{
    class KucoinPriceLevel : PriceLevel
    {
        public KucoinPriceLevel(decimal price, decimal qty)
        {
            Price = price;
            Qty = qty;
        }
    }
}
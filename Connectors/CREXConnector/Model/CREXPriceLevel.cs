using SharedDataStructures.Messages;

namespace CREXConnector.Model
{
    class CREXPriceLevel : PriceLevel
    {
        public CREXPriceLevel(decimal price, decimal size)
        {
            Price = price;
            Qty = size;
        }
    }
}
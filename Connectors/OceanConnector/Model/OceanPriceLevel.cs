using SharedDataStructures.Messages;

namespace OceanConnector.Model
{
    class OceanPriceLevel : PriceLevel
    {
        public OceanPriceLevel(decimal price)
        {
            Price = price;
            Qty   = 1;
        }
    }
}
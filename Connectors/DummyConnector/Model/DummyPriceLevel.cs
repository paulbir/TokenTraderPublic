using SharedDataStructures.Messages;

namespace DummyConnector.Model
{
    class DummyPriceLevel : PriceLevel
    {
        public DummyPriceLevel(decimal price, decimal qty)
        {
            Price = price;
            Qty = qty;
        }
    }
}
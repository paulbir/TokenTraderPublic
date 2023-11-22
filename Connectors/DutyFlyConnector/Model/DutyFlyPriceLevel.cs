using SharedDataStructures.Messages;

namespace DutyFlyConnector.Model
{
    class DutyFlyPriceLevel : PriceLevel
    {
        public DutyFlyPriceLevel(decimal price, decimal quantity)
        {
            Price = price;
            Qty = quantity;
        }
    }
}
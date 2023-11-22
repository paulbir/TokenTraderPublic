using SharedDataStructures.Messages;
using SharedTools;

namespace HitBTCConnector.Model
{
    class HitBTCPriceLevel : PriceLevel
    {
        public HitBTCPriceLevel(string price, string size)
        {
            Price = price.ToDecimal();
            Qty = size.ToDecimal();
        }
    }
}
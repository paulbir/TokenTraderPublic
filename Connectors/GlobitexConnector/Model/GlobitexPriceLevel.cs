using SharedDataStructures.Messages;
using SharedTools;

namespace GlobitexConnector.Model
{
    class GlobitexPriceLevel : PriceLevel
    {
        public GlobitexPriceLevel(string price, string size)
        {
            Price = price.ToDecimal();
            Qty = size.ToDecimal();
        }
    }
}
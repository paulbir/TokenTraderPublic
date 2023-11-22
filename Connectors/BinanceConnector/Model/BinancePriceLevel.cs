using SharedDataStructures.Messages;

namespace BinanceConnector.Model
{
    public class BinancePriceLevel : PriceLevel
    {
        public BinancePriceLevel(decimal price, decimal qty)
        {
            Price = price;
            Qty = qty;
        }
    }
}
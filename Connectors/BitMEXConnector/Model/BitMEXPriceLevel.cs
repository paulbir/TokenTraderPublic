using SharedDataStructures.Messages;

namespace BitMEXConnector.Model
{
    public class BitMEXPriceLevel : PriceLevel
    {
        public string Isin { get; }
        public string Side { get; }
        public BitMEXPriceLevel(string symbol, long id, string side, decimal size, decimal price)
        {
            Isin = symbol;
            Side = side;
            Id = id;
            Price = price;
            Qty = size;
        }
    }
}
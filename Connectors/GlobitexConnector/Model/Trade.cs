using SharedTools;

namespace GlobitexConnector.Model
{
    class Trade
    {
        public decimal Price { get; }
        public long Timestamp { get; }

        public Trade(string price, long timestamp)
        {
            Price = price.ToDecimal();
            Timestamp = timestamp;
        }
    }
}
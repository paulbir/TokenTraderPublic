using SharedDataStructures.Messages;
using TmexConnector.Model.Shared;

namespace TmexConnector.Model.Public.Data
{
    public class OrderBookLevel : PriceLevel
    {
        //public decimal Price { get; set; }
        //public long OrdersCount { get; set; }
        //public long Amount { get; set; }
        public TradeSide Side { get; set; }

        public OrderBookLevel(decimal p, long a, TradeSide s)
        {
            Price = p;
            Qty = a;
            Side = s;
        }
    }
}

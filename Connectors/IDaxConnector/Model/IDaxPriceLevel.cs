using SharedDataStructures.Messages;

namespace IDaxConnector.Model
{
    // ReSharper disable once InconsistentNaming
    class IDaxPriceLevel : PriceLevel
    {
        public OrderSide Side { get; }

        public IDaxPriceLevel(int orderSide, decimal price, decimal qty)
        {
            Side = orderSide == 1 ? OrderSide.Buy : OrderSide.Sell;
            Price = price;
            Qty = qty;
        }
    }
}
using SharedDataStructures.Messages;

namespace TokenTrader.OrderBook
{
    public class Order<TOrderId>
    {
        public decimal Price { get; private set; }
        public decimal Qty { get; private set; }
        public TOrderId Id { get; private set; }

        public void SetQty(decimal qty)
        {
            Qty = qty;
        }

        public void SetAll(decimal price, decimal qty, TOrderId id)
        {
            Price = price;
            Qty = qty;
            Id = id;
        }

        public override string ToString() => $"{Price};{Qty}";

        public static OrderSide InvertSide(OrderSide side)
        {
            return side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
        }
    }
}

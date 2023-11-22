using System;

namespace SharedDataStructures.Messages
{
    public class OrderMessage
    {
        public string OrderId { get; protected set; }
        public string Isin { get; protected set; }
        public OrderSide Side { get; protected set; }
        public string Status { get; protected set; }
        public decimal Price { get; protected set; }
        public decimal Qty { get; protected set; }
        public DateTime Timestamp { get; protected set; }
        public decimal TradeQty { get; protected set; }
        public decimal TradeFee { get; protected set; }
        public override string ToString() => $"{OrderId};{Isin};{Side};{Status};{Price};{Qty};{Timestamp};{TradeQty};{TradeFee}";

        public void UpdateQty(decimal newQty)
        {
            Qty = newQty;
        }
    }
}
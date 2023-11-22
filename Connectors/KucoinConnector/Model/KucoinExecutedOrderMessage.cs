using System;
using SharedDataStructures.Messages;

namespace KucoinConnector.Model
{
    class KucoinExecutedOrderMessage : OrderMessage
    {
        public string ExchangeOrderId { get; }

        public KucoinExecutedOrderMessage(string orderId,
                                  string symbol,
                                  string side,
                                  decimal price,
                                  decimal size,
                                  decimal fee,
                                  long createdAt)
        {
            ExchangeOrderId = orderId;
            Isin = symbol;
            Side = side == "buy" ? OrderSide.Buy : OrderSide.Sell;
            Price = price;
            Qty = 0;
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(createdAt).UtcDateTime;
            TradeQty = size;
            TradeFee = fee;
        }

        public void SetOrderIdAndQty(string clientOrderId, decimal qty)
        {
            OrderId = clientOrderId;
            Qty = qty;
        }
    }
}

using System;
using SharedDataStructures.Messages;

namespace WoortonV2Connector.Model
{
    class WoortonV2Trade : OrderMessage
    {
        public WoortonV2Trade(decimal price, decimal quantity, string side)
        {
            Price    = price;
            TradeQty = quantity;
            Side     = side == "BUY" ? OrderSide.Buy : OrderSide.Sell;
            Status   = "FILLED";
        }

        public void SetOrderFields(string isin, string orderId, DateTime timestamp, decimal orderQty)
        {
            Isin      = isin;
            OrderId   = orderId;
            Timestamp = timestamp;
            Qty       = orderQty;
        }
    }
}
using System;
using SharedDataStructures.Messages;

namespace FineryConnector.Model
{
    class FineryOrderMessage : OrderMessage
    {
        public FineryOrderMessage(string orderId, string isin, OrderSide side, decimal price, decimal qty, DateTime timestamp)
        {
            OrderId   = orderId;
            Isin      = isin;
            Side      = side;
            Price     = price;
            Qty       = qty;
            Timestamp = timestamp;
        }

        public void SetStatus(string status)
        {
            Status = status;
        }

        public void SetForTrade(decimal tradePrice, decimal tradeQty)
        {
            Price    = tradePrice;
            TradeQty = tradeQty;
            TradeFee = 0;
        }
    }
}
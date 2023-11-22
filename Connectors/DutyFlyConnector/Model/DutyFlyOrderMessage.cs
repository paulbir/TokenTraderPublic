using System;
using SharedDataStructures.Messages;

namespace DutyFlyConnector.Model
{
    class DutyFlyOrderMessage : OrderMessage
    {
        public string ExchangeOrderId { get; }
        public decimal CumulativeTradedQty { get; }

        public DutyFlyOrderMessage(string orderID,
                                   string side,
                                   decimal price,
                                   decimal quantity,
                                   decimal cumQuantity,
                                   string symbol,
                                   string status,
                                   DateTime updated)
        {
            ExchangeOrderId = orderID;
            Isin = symbol;
            Side = side == "buy" ? OrderSide.Buy : OrderSide.Sell;
            Status = status;
            Price = price;
            Qty = quantity;
            Timestamp = updated;
            CumulativeTradedQty = cumQuantity;
        }

        public void SetOrderId(string orderId)
        {
            OrderId = orderId;
        }

        public void SetTrade(decimal prevCumulativeTradedQty)
        {
            TradeQty = CumulativeTradedQty - prevCumulativeTradedQty;
            TradeFee = TradeQty * Price * 0.22m / 100;
        }
    }
}
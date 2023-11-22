using System;
using Newtonsoft.Json;
using SharedDataStructures.Messages;

namespace GlobitexConnector.Model
{
    class GlobitexExecutionReportMessage : OrderMessage
    {
        public string OrderRejectReason { get; }
        public decimal RemainingQty { get; private set; }

        public override string ToString() => $"{base.ToString()};{OrderRejectReason}";

        [JsonConstructor]
        public GlobitexExecutionReportMessage(string clientOrderId,
                                              string symbol,
                                              string side,
                                              string orderStatus,
                                              decimal price,
                                              decimal quantity,
                                              long timestamp,
                                              string orderRejectReason)
        {
            OrderId = clientOrderId;
            Isin = symbol;
            Side = side == "buy" ? OrderSide.Buy : OrderSide.Sell;
            Status = orderStatus;
            Price = price;
            Qty = quantity;
            RemainingQty = quantity;
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;
            OrderRejectReason = orderRejectReason;
        }

        public GlobitexExecutionReportMessage(string orderId,
                                              string isin,
                                              OrderSide side,
                                              string status,
                                              decimal price,
                                              decimal quantity,
                                              decimal remainingQty,
                                              DateTime timestamp,
                                              string orderRejectReason)
        {
            OrderId = orderId;
            Isin = isin;
            Side = side;
            Status = status;
            Price = price;
            Qty = quantity;
            RemainingQty = remainingQty;
            Timestamp = timestamp;
            OrderRejectReason = orderRejectReason;
        }

        public GlobitexExecutionReportMessage CreateDeepCopy()
        {
            return new GlobitexExecutionReportMessage(OrderId, Isin, Side, Status, Price, Qty, RemainingQty, Timestamp, OrderRejectReason);
        }

        public void UpdateOnTrade(decimal tradePrice, decimal tradeQty, decimal fee)
        {
            RemainingQty -= tradeQty;
            if (RemainingQty < 0) RemainingQty = 0;

            Price = tradePrice;
            TradeQty = tradeQty;
            TradeFee = fee;

            Status = RemainingQty == 0 ? "filled" : "partiallyFilled";
        }
    }
}
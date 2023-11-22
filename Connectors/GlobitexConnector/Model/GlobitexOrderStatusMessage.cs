using System;
using SharedDataStructures.Messages;
using SharedTools;

namespace GlobitexConnector.Model
{
    class GlobitexOrderStatusMessage : OrderMessage
    {
        public GlobitexOrderStatusMessage(string clientOrderId,
                                          string symbol,
                                          string side,
                                          string orderStatus,
                                          string orderPrice,
                                          string orderQuantity,
                                          long lastTimestamp,
                                          string cumQuantity)
        {
            OrderId = clientOrderId;
            Isin = symbol;
            Side = side == "buy" ? OrderSide.Buy : OrderSide.Sell;
            Status = orderStatus;
            Price = orderPrice.ToDecimal();
            Qty = orderQuantity.ToDecimal();
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(lastTimestamp).UtcDateTime;
            TradeQty = cumQuantity.ToDecimal();
        }
    }
}
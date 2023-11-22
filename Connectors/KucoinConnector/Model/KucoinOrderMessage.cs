using System;
using SharedDataStructures.Messages;

namespace KucoinConnector.Model
{
    class KucoinOrderMessage : OrderMessage
    {
        public string ExchangeOrderId { get; private set; }

        public KucoinOrderMessage(string id,
                                  string clientOid,
                                  string symbol,
                                  string side,
                                  decimal price,
                                  decimal size,
                                  decimal dealSize,
                                  decimal fee,
                                  long createdAt,
                                  bool isActive)
        {
            ExchangeOrderId = id;
            OrderId = string.IsNullOrEmpty(clientOid) ? id : clientOid;
            Isin = symbol;
            Side = side == "buy" ? OrderSide.Buy : OrderSide.Sell;

            if (dealSize > 0) Status = dealSize == size ? "filled" : "partially_filled";
            else Status = isActive ? "active" : "canceled";

            Price = price;
            Qty = size;
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(createdAt).UtcDateTime;
            TradeQty = dealSize;
            TradeFee =  fee;
        }

        public void SetStatus(string status)
        {
            Status = status;
        }

        public void DesreaseQty(decimal subtrahendQty)
        {
            Qty -= subtrahendQty;
        }
    }
}
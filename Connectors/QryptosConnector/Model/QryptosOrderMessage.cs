using System;
using SharedDataStructures.Messages;
using SharedTools;

namespace QryptosConnector.Model
{
    class QryptosOrderMessage : OrderMessage
    {
        public long ExchangeOrderId { get; }

        public QryptosOrderMessage(long id,
                            string currency_pair_code,
                            string side,
                            string status,
                            string price,
                            string quantity,
                            long updated_at,
                            string filled_quantity,
                            string order_fee)
        {
            ExchangeOrderId = id;
            Isin = currency_pair_code;
            Side = side == "buy" ? OrderSide.Buy : OrderSide.Sell;
            Status = status;
            Price = price.ToDecimal();
            Qty = quantity.ToDecimal();
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(updated_at).UtcDateTime;
            TradeQty = filled_quantity.ToDecimal();
            TradeFee = order_fee.ToDecimal();
        }

        public void SetOrderId(string orderId)
        {
            OrderId = orderId;
        }
    }
}
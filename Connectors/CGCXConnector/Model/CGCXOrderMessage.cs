using System;
using SharedDataStructures.Messages;

namespace CGCXConnector.Model
{
    class CGCXOrderMessage : OrderMessage
    {
        public long ExchangeOrderId { get; }
        public int IsinId { get; }
        public string ChangeReason { get; }

        public CGCXOrderMessage(string Side,
                                long OrderId,
                                decimal Price,
                                decimal Quantity,
                                int Instrument,
                                string OrderState,
                                decimal QuantityExecuted,
                                long ReceiveTime,
                                string ChangeReason)
        {
            IsinId = Instrument;
            ExchangeOrderId = OrderId;
            this.Side = Side == "Buy" ? OrderSide.Buy : OrderSide.Sell;
            Status = OrderState;
            this.Price = Price;
            Qty = Quantity;
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(ReceiveTime).UtcDateTime;
            TradeQty = QuantityExecuted;
            TradeFee = 0;

            this.ChangeReason = ChangeReason;
        }

        public void SetIsinAndId(string isin, string orderId)
        {
            Isin = isin;
            OrderId = orderId;
        }

        public CGCXOrderMessage CreateNewFromExecuted()
        {
            var newOrder = new CGCXOrderMessage(Side == OrderSide.Buy ? "Buy" : "Sell",
                                                ExchangeOrderId,
                                                Price,
                                                TradeQty,
                                                IsinId,
                                                "Working",
                                                0,
                                                ((DateTimeOffset)Timestamp).ToUnixTimeMilliseconds(),
                                                "NewInputAccepted");

            newOrder.SetIsinAndId(Isin, OrderId);
            return newOrder;
        }
    }
}
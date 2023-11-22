using System;
using Newtonsoft.Json;
using SharedDataStructures.Messages;

namespace CREXConnector.Model
{
    class CREXOrderMessage : OrderMessage
    {
        public long ExchangeOrderId { get; }
        public decimal QtyLeft { get; private set; }

        [JsonConstructor]
        public CREXOrderMessage(string instrument,
                                long order_id,
                                string client_transaction_id,
                                decimal price,
                                decimal size,
                                decimal active,
                                string direction,
                                string status,
                                DateTime changed)
        {
            ExchangeOrderId = order_id;
            OrderId = client_transaction_id;
            Isin = instrument;
            Side = direction == "BUY" ? OrderSide.Buy : OrderSide.Sell;
            Status = status;
            Price = price;
            Qty = size;
            Timestamp = changed;
            TradeQty = size - active;
            TradeFee = 0;

            QtyLeft = Qty;
        }

        public CREXOrderMessage(long exchangeOrderId,
                                string clientOrderId,
                                string isin,
                                OrderSide side,
                                string status,
                                decimal price,
                                decimal qty,
                                DateTime timestamp,
                                decimal tradeQty,
                                decimal tradeFee)
        {
            ExchangeOrderId = exchangeOrderId;
            OrderId = clientOrderId;
            Isin = isin;
            Side = side;
            Status = status;
            Price = price;
            Qty = qty;
            Timestamp = timestamp;
            TradeQty = tradeQty;
            TradeFee = tradeFee;

            QtyLeft = Qty;
        }

        public CREXOrderMessage DuplicateWithNewTrade(decimal tradeQty, decimal tradeFee)
        {
            return new CREXOrderMessage(ExchangeOrderId, OrderId, Isin, Side, Status, Price, Qty, Timestamp, tradeQty, tradeFee);
        }

        public void DecreaseQtyLeftOnTrade(decimal tradeQty)
        {
            QtyLeft -= tradeQty;
        }

        public void UpdateStatus(string status)
        {
            Status = status;
        }
    }
}
using System;
using SharedDataStructures.Messages;

namespace CoinFlexConnector.Model
{
    class CoinFlexOrderMessage : OrderMessage
    {
        //public string OrderId { get; protected set; }
        //public string Isin { get; protected set; }
        //public OrderSide Side { get; protected set; }
        //public string Status { get; protected set; }
        //public decimal Price { get; protected set; }
        //public decimal Qty { get; protected set; }
        //public DateTime Timestamp { get; protected set; }
        //public decimal TradeQty { get; protected set; }
        //public decimal TradeFee { get; protected set; }
        public CoinFlexOrderMessage(string  clientOrderId,
                                    string  isin,
                                    string  status,
                                    decimal priceUnscaled,
                                    decimal qtyUnscaled,
                                    long    timestamp,
                                    decimal scale)
        {
            OrderId = clientOrderId;
            Isin = isin;
            Side = qtyUnscaled > 0 ? OrderSide.Buy : OrderSide.Sell;
            Status = status;
            Price = priceUnscaled / scale;
            Qty = Math.Abs(qtyUnscaled) / scale;
            if (timestamp > 0) Timestamp = Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestamp / 1000).UtcDateTime;
        }

        public CoinFlexOrderMessage(string clientOrderId,
                                    string isin,
                                    OrderSide side,
                                    string status,
                                    decimal tradePriceUnscaled,
                                    decimal qtyLeftUnscaled,
                                    long timestamp,
                                    decimal tradeQtyUnscaled, 
                                    decimal tradeFeeUnscaled,
                                    decimal scale)
        {
            OrderId = clientOrderId;
            Isin = isin;
            Side = side;
            Status = status;
            Price = tradePriceUnscaled / scale;
            Qty = Math.Abs(qtyLeftUnscaled) / scale;
            if (timestamp > 0) Timestamp = Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestamp / 1000).UtcDateTime;
            TradeQty = tradeQtyUnscaled / scale;
            TradeFee = tradeFeeUnscaled / scale;
        }

        public CoinFlexOrderMessage(string orderId,
                                    string isin,
                                    OrderSide side,
                                    string status,
                                    decimal price,
                                    decimal qty,
                                    DateTime timestamp,
                                    decimal tradeQty,
                                    decimal tradeFee)
        {
            OrderId = orderId;
            Isin = isin;
            Side = side;
            Status = status;
            Price = price;
            Qty = qty;
            Timestamp = timestamp;
            TradeQty = tradeQty;
            TradeFee = tradeFee;
        }

        public CoinFlexOrderMessage CreateExecutionReport(decimal tradePriceUnscaled, decimal tradeQtyUnscaled, decimal tradeFeeUnscaled, decimal scale)
        {
            return new CoinFlexOrderMessage(OrderId,
                                            Isin,
                                            Side,
                                            OrderUpdateType.Matched.ToString(),
                                            tradePriceUnscaled / scale,
                                            Qty,
                                            Timestamp,
                                            tradeQtyUnscaled / scale,
                                            tradeFeeUnscaled / scale);
        }

        public void UpdateTimestamp(DateTime timestamp)
        {
            Timestamp = timestamp;
        }
    }
}
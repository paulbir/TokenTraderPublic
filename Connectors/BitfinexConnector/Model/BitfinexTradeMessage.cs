using System;

namespace BitfinexConnector.Model
{
    class BitfinexTradeMessage
    {
        public string Isin { get; }
        public long Id { get; }
        public long TimestampMs { get; }
        public decimal Qty { get; }
        public decimal Price { get; }
        public int Side { get; }

        public BitfinexTradeMessage(string isin, long id, long timestampMs, decimal qty, decimal price)
        {
            Isin = isin;
            Id = id;
            TimestampMs = timestampMs;
            Qty = Math.Abs(qty);
            Price = price;
            Side = qty > 0 ? 1 : 2;
        }
    }
}
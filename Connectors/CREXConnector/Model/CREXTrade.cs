namespace CREXConnector.Model
{
    class CREXTrade
    {
        public long ExchangeOrderId { get; set; }
        public decimal Qty { get; set; }
        public decimal Fee { get; set; }
        public string Isin { get; set; }
        public decimal Price { get; set; }

        public CREXTrade(string instrument, long order_id, decimal price, decimal size, decimal commission)
        {
            ExchangeOrderId = order_id;
            Isin = instrument;
            Price = price;
            Qty = size;
            Fee = Price * Qty * commission;
        }

        public override string ToString() => $"{Isin};{ExchangeOrderId};{Price};{Qty};{Fee}";
    }
}
namespace GlobitexConnector.Model
{
    class GlobitexTradeMessage
    {
        public string ClientOrderId { get; }
        public long TradeId { get; }
        public decimal TradePrice { get; }
        public decimal TradeQty { get; }
        public decimal TradeFee { get; }

        public GlobitexTradeMessage(long tradeId, string clientOrderId, decimal execPrice, decimal execQuantity, decimal fee)
        {
            TradeId = tradeId;
            ClientOrderId = clientOrderId;
            TradePrice = execPrice;
            TradeQty = execQuantity;
            TradeFee = fee;
        }
    }
}
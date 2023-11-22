namespace TmexConnector.Model.Shared.Data
{
    public class ClientDeal : ClientDealInfo
    {
        public string Symbol { get; set; }
        public long PortfolioId { get; set; }
        public long OrderId { get; set; }
        public long ExtId { get; set; }
        public OrderType OrderType { get; set; }
        public TradeSide Side { get; set; }
    }

    public class ClientDealInfo
    {
        public long DealId { get; set; }
        public long Amount { get; set; }
        public decimal Price { get; set; }
        public decimal Fee { get; set; }
        public long Timestamp { get; set; }
    }
}
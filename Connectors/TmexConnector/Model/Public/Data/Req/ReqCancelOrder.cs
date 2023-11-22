namespace TmexConnector.Model.Public.Data.Req
{
    public class ReqCancelOrder
    {
        public long PortfolioId { get; set; }

        /// <summary>
        /// Order id to cancel
        /// </summary>
        public long OrderId { get; set; }

        public long ExternalId { get; set; }

        public string Symbol { get; set; }
    }
}

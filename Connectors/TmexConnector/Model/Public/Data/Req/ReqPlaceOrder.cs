using TmexConnector.Model.Shared;

namespace TmexConnector.Model.Public.Data.Req
{
    public class ReqPlaceOrder
    {
        public OrderType Type { get; set; }

        public OrderFlags Flag { get; set; }
        
        /// <summary>
        /// Direction
        /// </summary>
        public TradeSide? Side { get; set; }

        /// <summary>
        /// Custom user-generated id (optional), not checked, not used in anything except response
        /// </summary>
        public long ExternalId { get; set; }

        /// <summary>
        /// Portfolio to use
        /// </summary>
        public long PortfolioId { get; set; }

        /// <summary>
        /// Asset symbol, case-insensitive
        /// </summary>
        public string Symbol { get; set; }

        /// <summary>
        /// Order price
        /// </summary>
        public decimal Price { get; set; }
        
        /// <summary>
        /// Trigger price: 
        /// &gt;0 - when last deal price is higher than stop price
        /// &lt;0 - when last deal price is lower than stop price
        /// </summary>
        public decimal StopPrice { get; set; }

        /// <summary>
        /// Amount (must be positive)
        /// </summary>
        public long Amount { get; set; }

        /// <summary>
        /// Unix-time in milliseconds.
        /// If request has this field non-zero, than core will 
        /// compare it to current time and reject order if 
        /// it is being processed later than specified time
        /// </summary>
        public long ValidUntil { get; set; }
    }
}
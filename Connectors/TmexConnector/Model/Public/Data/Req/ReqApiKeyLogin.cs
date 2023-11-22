namespace TmexConnector.Model.Public.Data.Req
{
    public class ReqApiKeyLogin
    {
        /// <summary>
        /// API key
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Authentication request payload, calculation: "AUTH"+nonce
        /// </summary>
        public string Payload { get; set; }

        /// <summary>
        /// Request signature, calculation: Base64-encoded HmacSHA512(pay, apiSecret)
        /// </summary>
        public string Signature { get; set; }

        /// <summary>
        /// String representation of ever increasing number
        /// </summary>
        public string Nonce { get; set; }

        /// <summary>
        /// Used only in websocket authorization.
        /// Specified value will be used in all WS requests
        /// requiring PortfolioId field.
        /// </summary>
        public long DefaultPortfolioId { get; set; }
    }
}
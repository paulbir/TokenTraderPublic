using Newtonsoft.Json;

namespace CREXConnector.Model
{
    class CREXTransaction
    {
        [JsonProperty("transaction_id")]
        public long TransactionId { get; set; }

        [JsonProperty("instrument")]
        public string Isin { get; set; }

        [JsonProperty("client_transaction_id")]
        public string ClientTransactionId { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("status_msg")]
        public string Message { get; set; }
    }
}
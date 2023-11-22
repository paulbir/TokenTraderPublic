using Newtonsoft.Json;

namespace OceanConnector.Model
{
    class RawOrderResponse
    {
        [JsonProperty("orderID")]
        public string OrderID { get; set; }

        [JsonProperty("filled")]
        public decimal TradeQty { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }
    }
}
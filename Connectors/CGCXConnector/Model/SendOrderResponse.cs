using Newtonsoft.Json;

namespace CGCXConnector.Model
{
    class SendOrderResponse
    {
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("errormsg")]
        public string Error { get; set; }

        [JsonProperty("OrderId")]
        public long OrderId { get; set; }
    }
}
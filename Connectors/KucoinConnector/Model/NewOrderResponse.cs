using Newtonsoft.Json;

namespace KucoinConnector.Model
{
    class NewOrderResponse
    {
        [JsonProperty("orderId")]
        public string ExchangeOrderId { get; set; }
    }
}

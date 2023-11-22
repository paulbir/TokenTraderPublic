using System.Collections.Generic;
using Newtonsoft.Json;

namespace KucoinConnector.Model
{
    class CanceledOrderResponse
    {
        [JsonProperty("cancelledOrderIds")]
        public List<string> ExchangeOrderIds { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
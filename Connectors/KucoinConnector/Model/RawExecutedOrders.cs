using System.Collections.Generic;
using Newtonsoft.Json;

namespace KucoinConnector.Model
{
    class RawExecutedOrders
    {
        [JsonProperty("items")]
        public List<KucoinExecutedOrderMessage> ExecutedOrders { get; set; }
    }
}

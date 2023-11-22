using System.Collections.Generic;
using Newtonsoft.Json;

namespace KucoinConnector.Model
{
    class RawActiveOrders
    {
        [JsonProperty("items")]
        public List<KucoinOrderMessage> Orders { get; set; }
    }
}

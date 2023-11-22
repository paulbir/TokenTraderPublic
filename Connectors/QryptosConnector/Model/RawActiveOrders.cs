using System.Collections.Generic;
using Newtonsoft.Json;

namespace QryptosConnector.Model
{
    class RawActiveOrders
    {
        [JsonProperty("models")]
        public List<QryptosOrderMessage> Orders { get; set; }
    }
}
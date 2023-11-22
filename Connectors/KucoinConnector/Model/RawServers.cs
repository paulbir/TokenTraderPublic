using System.Collections.Generic;
using Newtonsoft.Json;

namespace KucoinConnector.Model
{
    class RawServers
    {
        [JsonProperty("token")]
        public string Token { get; set; }

        [JsonProperty("instanceServers")]
        public List<Server> InstanceServers { get; set; }
    }
}

using Newtonsoft.Json;

namespace KucoinConnector.Model
{
    class Server
    {
        [JsonProperty("pingInterval")]
        public int PingInterval { get; set; }

        [JsonProperty("protocol")]
        public string Protocol { get; set; }

        [JsonProperty("endpoint")]
        public string BaseUri { get; set; }
    }
}

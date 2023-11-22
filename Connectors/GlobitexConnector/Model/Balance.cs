using Newtonsoft.Json;

namespace GlobitexConnector.Model
{
    class Balance
    {
        [JsonProperty("currency")]
        public string Currency { get; set; }

        [JsonProperty("available")]
        public decimal Available { get; set; }

        [JsonProperty("reserved")]
        public decimal Reserved { get; set; }
    }
}
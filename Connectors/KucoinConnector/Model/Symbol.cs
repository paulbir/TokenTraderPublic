using Newtonsoft.Json;

namespace KucoinConnector.Model
{
    class Symbol
    {
        [JsonProperty("symbol")]
        public string Isin { get; set; }

        [JsonProperty("baseIncrement")]
        public decimal MinOrderQty { get; set; }
    }
}
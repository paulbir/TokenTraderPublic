using Newtonsoft.Json;

namespace CREXConnector.Model
{
    class CREXInstrument
    {
        [JsonProperty("id")]
        public string Isin { get; set; }
    }
}
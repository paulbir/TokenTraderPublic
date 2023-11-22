using Newtonsoft.Json;

namespace CoinFlexConnector.Model
{
    class Asset
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("scale")]
        public long Scale { get; set; }
    }
}

using Newtonsoft.Json;

namespace XenaConnector.Model
{
    class RawBalance
    {
        [JsonProperty(Tags.Currency)]
        public string Currency { get; set; }

        [JsonProperty(Tags.AvailableBalance)]
        public decimal Available { get; set; }

        [JsonProperty(Tags.LockedBalance)]
        public decimal Reserved { get; set; }
    }
}
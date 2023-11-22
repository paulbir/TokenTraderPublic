using Newtonsoft.Json;

namespace XenaConnector.Model
{
    class RawMarketTrade
    {
        [JsonProperty(Tags.MDEntryPx)]
        public decimal Price { get; set; }

        [JsonProperty(Tags.MDEntrySize)]
        public decimal Qty { get; set; }

        [JsonProperty(Tags.TransactTime)]
        public long Timestamp { get; set; }
    }
}
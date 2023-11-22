using CoinFlexConnector.JSONExtensions;
using Newtonsoft.Json;

namespace CoinFlexConnector.Model
{
    enum OrderUpdateType
    {
        Opened,
        Modified,
        Closed,
        Matched
    }

    class  RawOrderUpdate
    {
        [JsonProperty("id")]
        public long ExchangeOrderId { get; set; }

        [JsonProperty("tonce")]
        public Settable<long> Tonce { get; set; }

        [JsonProperty("base")]
        public int BaseId { get; set; }

        [JsonProperty("counter")]
        public int CounterId { get; set; }

        [JsonProperty("quantity")]
        public long QtyUnscaled { get; set; }

        [JsonProperty("price")]
        public long PriceUnscaled { get; set; }

        [JsonProperty("time")]
        public long TimestampTicks { get; set; }
    }
}
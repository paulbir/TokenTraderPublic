using CoinFlexConnector.JSONExtensions;
using Newtonsoft.Json;

namespace CoinFlexConnector.Model
{
    class RawOrdersMatched
    {
        [JsonProperty("bid")]
        public long BidOrderId { get; set; }

        [JsonProperty("bid_tonce")]
        public Settable<long> BidTonce { get; set; }

        [JsonProperty("ask")]
        public long AskOrderId { get; set; }

        [JsonProperty("ask_tonce")]
        public Settable<long> AskTonce { get; set; }

        [JsonProperty("base")]
        public int BaseId { get; set; }

        [JsonProperty("counter")]
        public int CounterId { get; set; }

        [JsonProperty("quantity")]
        public long TradeQtyUnscaled { get; set; }

        [JsonProperty("price")]
        public long PriceUnscaled { get; set; }

        [JsonProperty("bid_rem")]
        public long BidQtyLeftUnscaled { get; set; }

        [JsonProperty("ask_rem")]
        public long AskQtyLeftUnscaled { get; set; }

        [JsonProperty("time")]
        public long TimestampTicks { get; set; }

        [JsonProperty("bid_base_fee")]
        public long BidBaseFee { get; set; }

        [JsonProperty("bid_counter_fee")]
        public long BidCounterFee { get; set; }

        [JsonProperty("ask_base_fee")]
        public long AskBaseFee { get; set; }

        [JsonProperty("ask_counter_fee")]
        public long AskCounterFee { get; set; }
    }
}
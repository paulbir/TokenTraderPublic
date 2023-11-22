using Newtonsoft.Json;

namespace BitfinexConnector.Model
{
    class BitfinexChannel
    {
        [JsonProperty("pair")]
        public string Pair { get; set; }

        [JsonProperty("channel")]
        public string Channel { get; set; }

        [JsonProperty("chanId")]
        public int ChannelId { get; set; }

        public BitfinexChannel(string pair, string channel, int chanId)
        {
            Pair = pair.ToLower();
            Channel = channel;
            ChannelId = chanId;
        }
    }
}
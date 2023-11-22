using Newtonsoft.Json;

namespace QryptosConnector.Model
{
    class Account
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("pusher_channel")]
        public string PusherChannel { get; set; }
    }
}
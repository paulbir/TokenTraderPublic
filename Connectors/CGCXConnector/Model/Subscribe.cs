using Newtonsoft.Json;

namespace CGCXConnector.Model
{
    class Subscribe
    {
        [JsonProperty("Subscribed")]
        public bool IsSubscribed { get; set; }
    }
}
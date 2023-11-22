using Newtonsoft.Json;

namespace CGCXConnector.Model
{
    class User
    {
        [JsonProperty("AccountId")]
        public int AccountId { get; set; }
    }
}
using Newtonsoft.Json;

namespace DutyFlyConnector.Model
{
    class LogonResponse
    {
        [JsonProperty("token")]
        public string AuthToken { get; set; }

        [JsonProperty("userId")]
        public string UserId { get; set; }
    }
}
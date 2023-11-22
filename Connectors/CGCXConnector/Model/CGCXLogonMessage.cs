using Newtonsoft.Json;

namespace CGCXConnector.Model
{
    class CGCXLogonMessage
    {        
        [JsonProperty("Authenticated")]
        public bool IsLogonSuccessfull { get; set; }

        [JsonProperty("User")]
        public User User { get; set; }
    }
}
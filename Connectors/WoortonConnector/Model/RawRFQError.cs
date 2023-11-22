using Newtonsoft.Json;

namespace WoortonConnector.Model
{
    class RawRFQError
    {
        [JsonProperty("message")]
        public string Message { get; set; }

        public override string ToString() => Message;
    }
}
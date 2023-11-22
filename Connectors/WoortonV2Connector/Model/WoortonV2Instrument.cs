using Newtonsoft.Json;

namespace WoortonV2Connector.Model
{
    class WoortonV2Instrument
    {
        [JsonProperty("name")]
        public string Isin { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("base")]
        public string Base { get; set; }

        [JsonProperty("quote")]
        public string Quote { get; set; }

        //public string WsIsin { get; private set; }

        //public void SetWsIsin(string wsIsin)
        //{
        //    WsIsin = wsIsin;
        //}
    }
}
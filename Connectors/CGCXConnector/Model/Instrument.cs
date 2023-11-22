using Newtonsoft.Json;

namespace CGCXConnector.Model
{
    class Instrument
    {
        [JsonProperty("InstrumentId")]
        public int Id { get; set; }

        [JsonProperty("Symbol")]
        public string Isin { get; set; }
    }
}
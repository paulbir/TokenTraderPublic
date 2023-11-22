using System.Collections.Generic;
using Newtonsoft.Json;

namespace WoortonConnector.Model
{
    class RawWsErrorDescription
    {
        [JsonProperty("instrument")]
        public List<string> InstrumentMessages { get; set; }
    }
}
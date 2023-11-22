using System.Collections.Generic;
using Newtonsoft.Json;

namespace WoortonConnector.Model
{
    class RawLevels
    {
        [JsonProperty("sell")]
        public List<WoortonPriceLevel> RawBids { get; set; }

        [JsonProperty("buy")]
        public List<WoortonPriceLevel> RawAsks { get; set; }
    }
}
using System.Collections.Generic;
using Newtonsoft.Json;

namespace WoortonV2Connector.Model
{
    class RawPriceLevels
    {
        [JsonProperty("sell")]
        public List<WoortonV2PriceLevel> Bids { get; set; }

        [JsonProperty("buy")]
        public List<WoortonV2PriceLevel> Asks { get; set; }
    }
}
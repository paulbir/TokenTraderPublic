using System.Collections.Generic;
using Newtonsoft.Json;

namespace DutyFlyConnector.Model
{
    class BalancesRaw
    {
        [JsonProperty("available")]
        public Dictionary<string, decimal> Available { get; set; }

        [JsonProperty("total")]
        public Dictionary<string, decimal> Total { get; set; }
    }
}
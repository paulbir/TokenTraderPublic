using System.Collections.Generic;
using Newtonsoft.Json;
using SharedDataStructures.Messages;

namespace CoinFlexConnector.Model
{
    class CoinFlexBalanceMessage : BalanceMessage
    {
        [JsonProperty("id")]
        public int AssetId { get; set; }

        [JsonProperty("available")]
        public decimal AvailableUnscaled { get; set; }

        [JsonProperty("reserved")]
        public decimal ReservedUnscaled { get; set; }

        public void SetValues(decimal scale, Dictionary<int, string> nameById)
        {
            if (!nameById.TryGetValue(AssetId, out string currency)) return;

            Currency = currency;
            Available = AvailableUnscaled / scale;
            Reserved = ReservedUnscaled / scale;
        }
    }
}
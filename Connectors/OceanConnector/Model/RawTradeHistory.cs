using System.Collections.Generic;
using Newtonsoft.Json;

namespace OceanConnector.Model
{
    class RawTradeHistory
    {
        [JsonProperty("history")]
        public List<OceanOrderMessage> TradeHistory { get; set; }
    }
}
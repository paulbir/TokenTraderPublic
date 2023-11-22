using System.Collections.Generic;
using Newtonsoft.Json;

namespace XenaConnector.Model
{
    class TradingLogon
    {
        [JsonProperty(Tags.Account)]
        public List<int> Accounts { get; set; }

        [JsonProperty(Tags.HeartBtInt)]
        public int HeartbeatIntervalSec { get; set; }

        [JsonProperty(Tags.SessionStatus)]
        public string SessionStatus { get; set; }

        [JsonProperty(Tags.RejectText)]
        public string RejectText { get; set; }
    }
}
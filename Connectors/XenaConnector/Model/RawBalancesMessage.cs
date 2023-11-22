using System.Collections.Generic;
using Newtonsoft.Json;

namespace XenaConnector.Model
{
    class RawBalancesMessage
    {
        [JsonProperty(Tags.Account)]
        public int Account { get; set; }

        [JsonProperty(Tags.RepeatingGroupBalance)]
        public List<RawBalance> Balances { get; set; }
    }
}
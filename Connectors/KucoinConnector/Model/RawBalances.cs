using System.Collections.Generic;
using Newtonsoft.Json;

namespace KucoinConnector.Model
{
    class RawBalances
    {
        [JsonProperty("datas")]
        public List<KucoinBalanceMessage> Balances { get; set; }

        [JsonProperty("pageNos")]
        public int NumPages { get; set; }
    }
}

using System.Collections.Generic;
using Newtonsoft.Json;

namespace GlobitexConnector.Model
{
    class Account
    {
        [JsonProperty("account")]
        public string AccountName { get; set; }

        [JsonProperty("balance")]
        public List<Balance> Balances { get; set; }
    }
}
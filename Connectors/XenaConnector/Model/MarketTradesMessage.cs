using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace XenaConnector.Model
{
    class MarketTradesMessage
    {
        [JsonProperty(Tags.Symbol)]
        public string Isin { get; set; }

        [JsonProperty(Tags.MDGrps)]
        public List<RawMarketTrade> Trades { get; set; }

        public decimal Last => Trades?.Aggregate((t1, t2) => t1.Timestamp > t2.Timestamp ? t1 : t2).Price ?? 0;
    }
}
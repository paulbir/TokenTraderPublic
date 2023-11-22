using System.Collections.Generic;
using Newtonsoft.Json;
using SharedDataStructures.Messages;

namespace XenaConnector.Model
{
    class XenaBookMessage : BookMessage
    {
        [JsonProperty(Tags.Symbol)]
        public string IsinRaw { get; set; }

        [JsonProperty(Tags.LastUpdateTime)]
        public long Timestamp { get; set; }

        [JsonProperty(Tags.MDGrps)]
        public List<XenaPriceLevel> PriceLevels { get; set; }

        public void SetBase()
        {
            Isin = IsinRaw;
            Sequence = Timestamp;

            Bids = new List<PriceLevel>();
            Asks = new List<PriceLevel>();

            if (PriceLevels == null) return;

            foreach (XenaPriceLevel priceLevel in PriceLevels)
            {
                priceLevel.SetBase();
                if (priceLevel.SideRaw == "0") Bids.Add(priceLevel);
                else Asks.Add(priceLevel);
            }
        }
    }
}
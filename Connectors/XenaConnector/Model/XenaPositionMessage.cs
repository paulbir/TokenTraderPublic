using Newtonsoft.Json;
using SharedDataStructures.Messages;

namespace XenaConnector.Model
{
    class XenaPositionMessage : PositionMessage
    {
        [JsonProperty(Tags.Symbol)]
        public string IsinRaw { get; set; }

        [JsonProperty(Tags.Side)]
        public int SideRaw { get; set; }

        [JsonProperty(Tags.Quantity)]
        public decimal QtyRaw { get; set; }

        [JsonProperty(Tags.PositionId)]
        public int PositionId { get; set; }

        [JsonProperty(Tags.TransactTime)]
        public long Timestamp { get; set; }

        public void SetBase()
        {
            Isin = IsinRaw;
            Qty = SideRaw == 1 ? QtyRaw : -1 * QtyRaw;
        }
    }
}
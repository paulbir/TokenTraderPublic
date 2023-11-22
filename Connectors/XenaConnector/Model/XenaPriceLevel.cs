using Newtonsoft.Json;
using SharedDataStructures.Messages;

namespace XenaConnector.Model
{
    class XenaPriceLevel : PriceLevel
    {
        [JsonProperty(Tags.MDEntryPx)]
        public decimal PriceRaw { get; set; }

        [JsonProperty(Tags.MDEntrySize)]
        public decimal QtyRaw { get; set; }

        [JsonProperty(Tags.MDEntryType)]
        public string SideRaw { get; set; }

        [JsonProperty(Tags.MDUpdateAction)]
        public string UpdateAction { get; set; }

        public void SetBase()
        {
            Price = PriceRaw;           
            
            //2 - delete, поэтому qty = 0. если это snapshot, то updateAction = null и Qty = QtyRaw 
            Qty = UpdateAction == "2" ? 0 : QtyRaw;
        }
    }
}
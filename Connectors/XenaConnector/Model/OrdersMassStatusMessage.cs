using System.Collections.Generic;
using Newtonsoft.Json;

namespace XenaConnector.Model
{
    class OrdersMassStatusMessage
    {
        [JsonProperty(Tags.Account)]
        public int Account { get; set; }

        [JsonProperty(Tags.Orders)]
        public List<XenaOrderMessage> ActiveOrders { get; set; }
    }
}
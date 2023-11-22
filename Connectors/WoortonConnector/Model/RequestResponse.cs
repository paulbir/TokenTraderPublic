using System.Collections.Generic;
using Newtonsoft.Json;

namespace WoortonConnector.Model
{
    class RequestResponse
    {
        [JsonProperty("request_id")]
        public string RequestId { get; set; }

        [JsonProperty("amount")]
        public decimal Qty { get; set; }

        [JsonProperty("price")]
        public decimal Price { get; set; }

        [JsonProperty("total")]
        public decimal Total { get; set; }

        [JsonProperty("instrument")]
        public string Isin { get; set; }

        [JsonProperty("direction")]
        public string Side { get; set; }

        [JsonProperty("errors")]
        public List<RawRFQError> Errors { get; set; }

        public string FlattenedErrors => string.Join(';', Errors);
    }
}
using Newtonsoft.Json;

namespace QryptosConnector.Model
{
    class Product
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("currency_pair_code")]
        public string Isin { get; set; }

        [JsonProperty("quoted_currency")]
        public string QuotedCurrency { get; set; }
    }
}
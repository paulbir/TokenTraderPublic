using Newtonsoft.Json;

namespace CGCXConnector.Model
{
    class CGCXProductMessage
    {
        [JsonProperty("Product")]
        public string Currency { get; set; }

        [JsonProperty("ProductId")]
        public int CurrencyId { get; set; }
    }
}
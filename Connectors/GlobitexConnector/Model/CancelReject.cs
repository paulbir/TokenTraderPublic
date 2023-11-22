using Newtonsoft.Json;

namespace GlobitexConnector.Model
{
    class CancelReject
    {
        [JsonProperty("clientOrderId")]
        public string ClientOrderId { get; set; }

        [JsonProperty("cancelRequestClientOrderId")]
        public string CancelRequestClientOrderId { get; set; }

        [JsonProperty("rejectReasonCode")]
        public string RejectReasonCode { get; set; }

        [JsonProperty("account")]
        public string Account { get; set; }

        public override string ToString() =>
            $"clientOrderId={ClientOrderId};cancelRequestClientOrderId={CancelRequestClientOrderId};" +
            $"rejectReasonCode={RejectReasonCode};account={Account}";
    }
}
using Newtonsoft.Json;
using SharedDataStructures.Messages;

namespace GlobitexConnector.Model
{
    class RawExecutionReport<T> where T : OrderMessage
    {
        [JsonProperty("ExecutionReport")]
        public T ExecutionReport { get; set; }

        [JsonProperty("CancelReject")]
        public CancelReject Reject { get; set; }
    }
}
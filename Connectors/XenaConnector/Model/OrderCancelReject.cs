using Newtonsoft.Json;

namespace XenaConnector.Model
{
    class OrderCancelReject
    {
        public string Reason { get; private set; }

        [JsonProperty(Tags.OrigClOrdID)]
        public string OrderId { get; set; }

        [JsonProperty(Tags.CxlRejReason)]
        public string ReasonCode { get; set; }

        [JsonProperty(Tags.RejectText)]
        public string RejectText { get; set; }

        public void SetReason()
        {
            switch (ReasonCode)
            {
                case "0":
                    Reason = "TooLateToCancel";
                    break;

                case "1":
                    Reason = "UnknownOrder";
                    break;

                case "3":
                    Reason = "OrderAlreadyInPendingStatus";
                    break;

                case "6":
                    Reason = "DuplicateClOrdID";
                    break;

                case "99":
                    Reason = "Other";
                    break;

                default:
                    Reason = ReasonCode;
                    break;
            }
        }
    }
}
using SharedDataStructures;
using SharedDataStructures.Messages;

namespace WoortonV2Connector.Model
{
    class WoortonV2ErrorMessage : ErrorMessage
    {
        public WoortonV2ErrorMessage(RequestError code, string message, string description)
        {
            Code        = (int)code;
            Message     = message;
            Description = description;
            IsCritical = true;
        }

        public WoortonV2ErrorMessage(RequestError code, string message, string description, bool isCritical)
        {
            Code        = (int)code;
            Message     = message;
            Description = description;
            IsCritical = isCritical;
        }
    }
}
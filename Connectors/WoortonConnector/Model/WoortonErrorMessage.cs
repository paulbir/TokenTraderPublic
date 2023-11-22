using SharedDataStructures;
using SharedDataStructures.Messages;

namespace WoortonConnector.Model
{
    class WoortonErrorMessage : ErrorMessage
    {
        public WoortonErrorMessage(RequestError code, string message, string description)
        {
            Code        = (int)code;
            Message     = message;
            Description = description;
            IsCritical = true;
        }
    }
}
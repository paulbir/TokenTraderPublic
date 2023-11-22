using SharedDataStructures.Messages;

namespace GlobitexConnector.Model
{
    class GlobitexErrorMessage : ErrorMessage
    {
        public GlobitexErrorMessage(int code, string message, string data, bool isCritical = false)
        {
            Code = code;
            Message = message;
            Description = data ?? "";

            //if (message.Contains("Internal error")) IsCritical = true;
            IsCritical = isCritical;
        }
    }
}
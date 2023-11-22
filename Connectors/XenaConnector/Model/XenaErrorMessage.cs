using SharedDataStructures.Messages;

namespace XenaConnector.Model
{
    class XenaErrorMessage : ErrorMessage
    {
        public XenaErrorMessage(int code, string message, string description)
        {
            Code = code;
            Message = message;
            Description = description;
        }
    }
}
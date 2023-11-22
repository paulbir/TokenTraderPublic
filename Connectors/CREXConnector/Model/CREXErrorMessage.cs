using SharedDataStructures.Messages;

namespace CREXConnector.Model
{
    class CREXErrorMessage : ErrorMessage
    {
        public CREXErrorMessage(int code, string message, string description)
        {
            Code = code;
            Message = message;
            Description = description;
        }
    }
}
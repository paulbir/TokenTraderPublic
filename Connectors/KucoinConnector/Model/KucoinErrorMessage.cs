using SharedDataStructures.Messages;

namespace KucoinConnector.Model
{
    class KucoinErrorMessage : ErrorMessage
    {
        public KucoinErrorMessage(int code, string message, string description)
        {
            Code = code;
            Message = message;
            Description = description;
        }
    }
}

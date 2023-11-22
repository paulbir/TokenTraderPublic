using SharedDataStructures.Messages;

namespace TmexConnector.Model
{
    class TmexErrorMessage : ErrorMessage
    {
        public TmexErrorMessage(int code, string message, string description)
        {
            Code = code;
            Message = message;
            Description = description;
        }
    }
}
using SharedDataStructures.Messages;

namespace CGCXConnector.Model
{
    class CGCXErrorMessage : ErrorMessage
    {
        public CGCXErrorMessage(int code, string message, string description)
        {
            Code = code;
            Message = message;
            Description = description;
        }
    }
}
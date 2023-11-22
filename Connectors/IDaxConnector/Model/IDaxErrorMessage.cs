using SharedDataStructures.Messages;

namespace IDaxConnector.Model
{
    // ReSharper disable once InconsistentNaming
    class IDaxErrorMessage : ErrorMessage
    {
        public IDaxErrorMessage(int code, string message, string description)
        {
            Code = code;
            Message = message;
            Description = description;
        }
    }
}
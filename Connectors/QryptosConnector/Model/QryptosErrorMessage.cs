using SharedDataStructures.Messages;

namespace QryptosConnector.Model
{
    class QryptosErrorMessage : ErrorMessage
    {
        public QryptosErrorMessage(int code, string message, string description)
        {
            Code = code;
            Message = message;
            Description = description;
        }
    }
}
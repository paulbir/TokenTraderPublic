using SharedDataStructures.Messages;

namespace DeribitConnector.Model
{
    class DeribitErrorMessage : ErrorMessage
    {
        public DeribitErrorMessage(int code, string message, string description)
        {
            Code = code;
            Message = message;
            Description = description;
        }
    }
}
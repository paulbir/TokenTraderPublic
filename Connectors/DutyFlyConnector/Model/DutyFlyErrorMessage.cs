using SharedDataStructures.Messages;

namespace DutyFlyConnector.Model
{
    class DutyFlyErrorMessage : ErrorMessage
    {
        public DutyFlyErrorMessage(int code, string message, string description)
        {
            Code = code;
            Message = message;
            Description = description;
        }
    }
}
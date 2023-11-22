using SharedDataStructures.Messages;

namespace HitBTCConnector.Model
{
    class HitBTCErrorMessage : ErrorMessage
    {
        public HitBTCErrorMessage(int code, string message, string description)
        {
            Code = code;
            Message = message;
            Description = description;
        }
    }
}
using SharedDataStructures.Messages;

namespace BitstampConnector.Model
{
    class BitstampErrorMessage : ErrorMessage
    {
        public BitstampErrorMessage(int code, string message, string description)
        {
            Code = code;
            Message = message;
            Description = description;
        }
    }
}
using SharedDataStructures.Messages;

namespace CoinFlexConnector.Model
{
    class CoinFlexErrorMessage : ErrorMessage
    {
        public CoinFlexErrorMessage(int code, string message, string description)
        {
            Code = code;
            Message = message;
            Description = description;
        }
    }
}
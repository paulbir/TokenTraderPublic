using SharedDataStructures.Messages;

namespace BinanceConnector.Model
{
    class BinanceErrorMessage : ErrorMessage
    {
        public BinanceErrorMessage(int code, string message, string description)
        {
            Code = code;
            Message = message;
            Description = description;
        }
    }
}
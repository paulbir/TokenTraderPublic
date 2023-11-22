using SharedDataStructures.Messages;

namespace BitfinexConnector.Model
{
    class BitfinexErrorMessage : ErrorMessage
    {
        public BitfinexErrorMessage(string channel, string pair, string prec, string symbol, string msg, int code)
        {
            Code = code;
            Message = msg;
            Description = $"Request: channel={channel};pair={pair};precision={prec};symbol={symbol} failed.";
        }
    }
}
using System;

namespace SharedDataStructures.Exceptions
{
    public class ExchangeApiException : Exception
    {
        public ExchangeApiException() {}

        public ExchangeApiException(string message)
            : base(message) {}

        public ExchangeApiException(string format, params object[] args)
            : base(string.Format(format, args)) {}

        public ExchangeApiException(string message, Exception innerException)
            : base(message, innerException) {}

        public ExchangeApiException(string format, Exception innerException, params object[] args)
            : base(string.Format(format, args), innerException) {}
    }
}
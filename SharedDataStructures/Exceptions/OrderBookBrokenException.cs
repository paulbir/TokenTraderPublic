using System;

namespace SharedDataStructures.Exceptions
{
    public class OrderBookBrokenException : Exception
    {
        public OrderBookBrokenException() {}

        public OrderBookBrokenException(string message)
            : base(message) {}

        public OrderBookBrokenException(string format, params object[] args)
            : base(string.Format(format, args)) {}

        public OrderBookBrokenException(string message, Exception innerException)
            : base(message, innerException) {}

        public OrderBookBrokenException(string format, Exception innerException, params object[] args)
            : base(string.Format(format, args), innerException) {}
    }
}
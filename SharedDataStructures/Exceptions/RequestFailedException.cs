using System;

namespace SharedDataStructures.Exceptions
{
    public class RequestFailedException : Exception
    {
        public RequestFailedException() {}

        public RequestFailedException(string message)
            : base(message) {}

        public RequestFailedException(string format, params object[] args)
            : base(string.Format(format, args)) {}

        public RequestFailedException(string message, Exception innerException)
            : base(message, innerException) {}

        public RequestFailedException(string format, Exception innerException, params object[] args)
            : base(string.Format(format, args), innerException) {}
    }
}
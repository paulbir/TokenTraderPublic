using System;

namespace SharedDataStructures.Exceptions
{
    public class ConfigErrorsException : Exception
    {
        public ConfigErrorsException() {}

        public ConfigErrorsException(string message)
            : base(message) {}

        public ConfigErrorsException(string format, params object[] args)
            : base(string.Format(format, args)) {}

        public ConfigErrorsException(string message, Exception innerException)
            : base(message, innerException) {}

        public ConfigErrorsException(string format, Exception innerException, params object[] args)
            : base(string.Format(format, args), innerException) {}
    }
}
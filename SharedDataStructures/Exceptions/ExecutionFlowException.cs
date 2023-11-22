using System;

namespace SharedDataStructures.Exceptions
{
    public class ExecutionFlowException : Exception
    {
        public ExecutionFlowException() {}

        public ExecutionFlowException(string message)
            : base(message) {}

        public ExecutionFlowException(string format, params object[] args)
            : base(string.Format(format, args)) {}

        public ExecutionFlowException(string message, Exception innerException)
            : base(message, innerException) {}

        public ExecutionFlowException(string format, Exception innerException, params object[] args)
            : base(string.Format(format, args), innerException) {}
    }
}
namespace WoortonConnector.Model
{
    class RawWsError
    {
        public int    ErrorCode { get; }
        public string Message   { get; }

        public RawWsError(int error_code, RawWsErrorDescription errors)
        {
            ErrorCode = error_code;
            Message   = string.Join(';', errors.InstrumentMessages);
        }
    }
}
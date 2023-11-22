namespace TmexConnector.Model.Shared
{
    public class ApiError
    {
        public ApiError() { }

        public ApiError(ErrorCode code, string message)
        {
            Code = code;
            Message = message;
        }

        public ErrorCode Code { get; set; }
        public string Message { get; set; }
    }
}

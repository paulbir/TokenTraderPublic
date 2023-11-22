namespace SharedDataStructures.Messages
{
    public class ErrorMessage
    {
        public int Code { get; protected set; }
        public string Message { get; protected set; }
        public string Description { get; protected set; }
        public bool IsCritical { get; protected set; } = false;
    }
}
namespace SharedDataStructures.Messages
{
    public class BalanceMessage
    {
        public string Currency { get; protected set; }
        public decimal Available { get; protected set; }
        public decimal Reserved { get; protected set; }
        public override string ToString() => $"{Currency};{Available};{Reserved}";
    }
}
namespace SharedDataStructures.Messages
{
    public class PositionMessage
    {
        public string Isin { get; protected set; }
        public decimal Qty { get; protected set; }
        public override string ToString() => $"{Isin};{Qty}";
    }
}
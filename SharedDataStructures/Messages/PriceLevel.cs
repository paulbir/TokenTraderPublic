namespace SharedDataStructures.Messages
{
    public class PriceLevel
    {
        long id;
        decimal price;

        public long Id
        {
            get => id == 0 ? Price.GetHashCode() : id;
            protected set => id = value;
        }

        public decimal Price
        {
            get => price;
            protected set
            {
                if (value < 0)
                {
                    price       = -1 * value;
                    ApplyMethod = PriceLevelApplyMethod.OrderLog;
                }
                else price = value;
            }
        }
        public decimal Qty { get; protected set; }
        public PriceLevelApplyMethod ApplyMethod { get; protected set; } = PriceLevelApplyMethod.Straight;

        public override string ToString() => $"Id={Id};Price={Price};Qty={Qty}";
    }
}
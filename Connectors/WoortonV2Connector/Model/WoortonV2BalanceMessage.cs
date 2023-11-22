using SharedDataStructures.Messages;

namespace WoortonV2Connector.Model
{
    class WoortonV2BalanceMessage : BalanceMessage
    {
        public decimal Max { get; }
        public decimal Min { get; }

        public WoortonV2BalanceMessage(string currency_id, decimal value_max, decimal value_min)
        {
            Currency  = currency_id;
            Available = value_max;
            Reserved  = 0;

            Max = value_max;
            Min = value_min;
        }
    }
}
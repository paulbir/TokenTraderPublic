using SharedDataStructures.Messages;

namespace OceanConnector.Model
{
    class OceanBalanceMessage : BalanceMessage
    {
        public OceanBalanceMessage(string currency, decimal available)
        {
            Currency  = currency;
            Available = available;
            Reserved  = 0;
        }
    }
}
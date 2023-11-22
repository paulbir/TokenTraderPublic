using SharedDataStructures.Messages;

namespace IDaxConnector.Model
{
    // ReSharper disable once InconsistentNaming
    class IDaxBalanceMessage : BalanceMessage
    {
        public IDaxBalanceMessage(string coinCode, decimal available, decimal frozen)
        {
            Currency = coinCode;
            Available = available;
            Reserved = frozen;
        }
    }
}
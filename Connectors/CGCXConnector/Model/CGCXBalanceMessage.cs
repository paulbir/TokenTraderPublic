using SharedDataStructures.Messages;

namespace CGCXConnector.Model
{
    class CGCXBalanceMessage : BalanceMessage
    {
        public CGCXBalanceMessage(string ProductSymbol, decimal Amount, decimal Hold)
        {
            Currency = ProductSymbol;
            Available = Amount - Hold;
            Reserved = Hold;
        }
    }
}
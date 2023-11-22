namespace TmexConnector.Model.Shared
{
    public enum OrderType : short
    {
        Limit = 0,
        Market = 1,
        
        Liquidation = 100, // internal use only
        Settlement = 101, // internal use only
        Confiscation = 102, // internal use only
    }
}
namespace TmexConnector.Model.Shared
{
    public enum OrderFlags
    {
        None = 0,

        /// <summary>
        /// Cancel amount left after matching without placing to orderbook
        /// </summary>
        ImmediateOrCancel = 1,

        /// <summary>
        /// 
        /// </summary>
        PostOnly = 2
    }
}
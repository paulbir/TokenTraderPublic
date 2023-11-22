namespace TmexConnector.Model.Shared
{
    public enum OrderState : short
    {
        Pending = 0,
        Active = 1,

        /// <summary>
        /// Terminal state.
        /// </summary>
        Cancelled = 2,

        /// <summary>
        /// Terminal state.
        /// </summary>
        Filled = 3,

        /// <summary>
        /// Terminal state. Stop-order has been executed, child order - accepted
        /// </summary>
        ChildOrderPlaced = 4,

        /// <summary>
        /// Terminal state. Stop-order has been executed, child order - rejected
        /// </summary>
        ChildOrderRejected = 5,
    }
}
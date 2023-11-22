using System;

namespace TmexConnector.Model.Shared
{
    [Flags]
    public enum TradeSide : byte
    {
        Buy = 0,
        Sell = 1
    }
}

using System.Threading;

namespace TokenTrader.Interfaces
{
    interface ICancellationTokenProvider
    {
        CancellationToken Token { get; }
    }
}
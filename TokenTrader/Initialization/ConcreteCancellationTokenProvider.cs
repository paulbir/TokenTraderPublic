using System.Threading;
using TokenTrader.Interfaces;

namespace TokenTrader.Initialization
{
    class ConcreteCancellationTokenProvider : ICancellationTokenProvider
    {
        public CancellationToken Token { get; }

        public ConcreteCancellationTokenProvider(CancellationTokenSource source)
        {
            Token = source.Token;
        }
    }
}
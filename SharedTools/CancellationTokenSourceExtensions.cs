using System.Threading;

namespace SharedTools
{
    public static class CancellationTokenSourceExtensions
    {
        public static CancellationToken NewPairedTimeoutToken(this CancellationTokenSource baseSource, int timeoutMs)
        {
            return CancellationTokenSource.CreateLinkedTokenSource(baseSource.Token, new CancellationTokenSource(timeoutMs).Token).Token;
        }
    }
}

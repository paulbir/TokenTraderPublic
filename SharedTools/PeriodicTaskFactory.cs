using System;
using System.Threading;
using System.Threading.Tasks;

namespace SharedTools
{
    public static class PeriodicTaskFactory
    {
        public static async Task Run(Action action, int millisecondsDelay, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!cancellationToken.IsCancellationRequested) await Task.Run(action, cancellationToken);

                await Task.Delay(millisecondsDelay, cancellationToken);
            }
        }

        public static Task Run(Action action, int millisecondsDelay)
        {
            return Run(action, millisecondsDelay, CancellationToken.None);
        }

        public static async Task RunOnce(Action action, int millisecondsDelay, CancellationToken cancellationToken)
        {
            await Task.Delay(millisecondsDelay, cancellationToken);

            if (!cancellationToken.IsCancellationRequested) await Task.Run(action, cancellationToken);
        }
    }
}
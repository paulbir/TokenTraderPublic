using System;
using System.Threading;
using System.Threading.Tasks;
using SharedDataStructures.Messages;
using SharedTools;
using SharedTools.Interfaces;
using TokenTrader.Initialization;
using TokenTrader.Interfaces;
using TokenTrader.OrderBook;

namespace TokenTrader
{
    class Program
    {
        static          CancellationTokenSource parentTokenSource = new CancellationTokenSource();
        static          ILogger                 logger;
        static          ITradeModel             tradeModel;
        static volatile bool                    gotException;
        static volatile bool                    calledDoStop;
        static          string                  unhandledExceptionMessage;

        static OrderSide tradeSide   = OrderSide.Buy;
        static bool      isTradeSent = false;

        static UnlimitedOrderBook<long> book = new UnlimitedOrderBook<long>(5, false, 10000);

        static async Task Main(string[] args)
        {
            //ManualTokenTrading();
            //return;

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            Initializer.Register();

            parentTokenSource = Initializer.ParentTokenSource;
            logger            = Initializer.Container.GetInstance<ILogger>();

            logger.Enqueue("Initialization finished. Starting.");

            tradeModel = Initializer.Container.GetInstance<ITradeModel>();

            Console.CancelKeyPress += Console_CancelKeyPress;

            try { await tradeModel.Start(); }
            catch (OperationCanceledException) { logger?.Enqueue("Caught OperationCanceledException. Dropping."); }
            finally
            {
                logger?.Enqueue("Going to call DoStop finally.");
                DoStop();
            }

            logger?.Enqueue("Going exit Main.");
            logger?.WriteQueueToOutput();

            //await DoStop();
        }

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            logger?.Enqueue("Entered CurrentDomain_UnhandledException.");
            if (gotException)
            {
                logger?.Enqueue("Already got unhandled exception. Exit CurrentDomain_UnhandledException.");
                return;
            }

            gotException = true;

            var ex = (Exception)args.ExceptionObject;

            string extendedExceptionMessage = ex is AggregateException aex ? aex.Flatten().MakeString() : ex.MakeString();
            unhandledExceptionMessage = extendedExceptionMessage;

            if (logger == null) Console.WriteLine(extendedExceptionMessage);
            else logger.Enqueue(extendedExceptionMessage);

            logger?.Enqueue("Going call DoStop UnhandledException.");
            DoStop();

            logger?.Enqueue("Going to write output queue and Exit Environment.");
            logger?.WriteQueueToOutput();
            Environment.Exit(1);
        }

        static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs args)
        {
            logger?.Enqueue("Entered Console_CancelKeyPress.");
            args.Cancel = true;

            logger?.Enqueue("Going to cancel parentTokenSource.");
            parentTokenSource.Cancel();
        }

        static void DoStop()
        {
            logger?.Enqueue("Entered DoStop.");

            if (calledDoStop)
            {
                logger?.Enqueue("DoStop was already called. Going to exit DoStop.");
                return;
            }

            calledDoStop = true;

            logger?.Enqueue($"Going to Stop trade model with gotException={gotException}.");
            tradeModel.Stop(gotException, unhandledExceptionMessage);

            if (!parentTokenSource.IsCancellationRequested)
            {
                logger?.Enqueue("Going to cancel parentTokenSource.");
                parentTokenSource.Cancel();
            }

            logger?.Enqueue("Going to write output queue.");
            logger?.WriteQueueToOutput();
        }

        static void ManualTokenTrading() { }
    }
}
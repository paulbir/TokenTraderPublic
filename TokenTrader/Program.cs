using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FineryConnector;
using OceanConnector;
using SharedDataStructures;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SharedTools;
using SharedTools.Interfaces;
using TokenTrader.Initialization;
using TokenTrader.Interfaces;
using TokenTrader.OrderBook;
using WoortonConnector;
using WoortonV2Connector;
using DeribitConnector;

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
            ManualTokenTrading();
            return;

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            Initializer.Register();

            parentTokenSource = Initializer.ParentTokenSource;
            logger            = Initializer.Container.GetInstance<ILogger>();

            logger.Enqueue("Initialization finished. Starting.");

            tradeModel = Initializer.Container.GetInstance<ITradeModel>();

            Console.CancelKeyPress += Console_CancelKeyPress;

            try { await tradeModel.Start(); }
            catch (OperationCanceledException)
            {
                logger?.Enqueue("Caught OperationCanceledException. Dropping.");
            }
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

            //logger?.Enqueue("Going to cancel parentTokenSource.");
            //parentTokenSource?.Cancel();

            //logger?.Enqueue("Going to wait 50ms.");
            //Thread.Sleep(50);

            //logger?.Enqueue("Going to WriteQueueToOutput.");
            //logger?.WriteQueueToOutput();

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

        static void ManualTokenTrading()
        {
        }

        static void Client_ExecutionReportArrived(object sender, OrderMessage e)
        {
            Console.WriteLine($"OrderTrade: {e.OrderId};{e.Isin};{e.Side};{e.Price};{e.TradeQty};{e.TradeFee}");
        }

        static void Client_OrderCanceled(object sender, OrderMessage e)
        {
            Console.WriteLine($"CancelOrder: {e}");
        }

        static void Client_NewOrderAdded(object sender, OrderMessage e)
        {
            Console.WriteLine($"AddOrder: {e}");
        }

        static void Client_ErrorOccured(object sender, ErrorMessage e)
        {
            Console.WriteLine($"{(RequestError)e.Code} | {e.Message} | {e.Description}");
        }

        static void Client_ActiveOrdersListArrived(object sender, List<OrderMessage> e)
        {
            foreach (OrderMessage order in e) Console.WriteLine(order);
        }

        static void Client_Connected(object sender, EventArgs e) { }

        static void Client_BookUpdateArrived(object sender, BookMessage e)
        {
            BookHelpers.ApplyUpdate(e, book, 5, true, logger);

            decimal bid = book.BestBid;
            decimal bidQty = book.BestBidQty;
            //bid = Math.Floor(bid * 100) / 100 - 100;

            decimal ask = book.BestAsk;
            decimal askQty = book.BestAskQty;
            //ask = Math.Ceiling(ask * 100) / 100 + 100;
            if (book.PrevSentBestBid != bid || book.PrevSentBestAsk != ask)
            {
                Console.Clear();
                Console.WriteLine($"{ask} {askQty}\n{bid} {bidQty}");

                book.SetPrevSentBest();
            }

            //string bookString = book.AsksString + "\n\n" + book.BidsString;
            //long hash = bookString.GetPositiveStableHashCode();
            //if (hash <= 0) Console.WriteLine();
            //Console.Clear();
            //Console.WriteLine(bookString);
        }

        static void Client_BookSnapshotArrived(object sender, BookMessage e)
        {
            BookHelpers.ApplySnapshot(e, book, 5, true, logger);

            decimal bid = e.Bids[0].Price;
            decimal bidQty = e.Bids[0].Qty;
            //bid = Math.Floor(bid * 100) / 100 - 100;

            decimal ask = e.Asks[0].Price;
            decimal askQty = e.Asks[0].Qty;
            //ask = Math.Ceiling(ask * 100) / 100 + 100;
            //Console.Clear();
            //Console.WriteLine($"{bid}  {ask}");

            //if (isTradeSent) return;
            //isTradeSent = true;

            //decimal   slippageFrac = 0.02m;
            //OrderSide side         = OrderSide.Buy;
            //decimal price = ask + 20;

            ////if (tradeSide == OrderSide.Buy) price = ask * (1 + slippageFrac);
            ////else price = bid * (1 - slippageFrac);

            //var client = (IHedgeConnector)sender;
            //client.AddHedgeOrder($"myneworder_1", "BTCEUR", side, price, 0.001m, slippageFrac, 1);

            //tradeSide = tradeSide == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
        }
         
        static void Client_PositionArrived(object sender, List<PositionMessage> e)
        {
            foreach (PositionMessage positionMessage in e) { Console.WriteLine($"position:{positionMessage.Isin};{positionMessage.Qty}"); }
        }

        static void Client_BalanceArrived(object sender, List<BalanceMessage> e)
        {
            foreach (BalanceMessage balanceMessage in e)
            {
                Console.WriteLine($"balance:{balanceMessage.Currency};{balanceMessage.Available};{balanceMessage.Reserved}");
            }
        }

        static void Client_LimitArrived(object sender, List<LimitMessage> e)
        {
            foreach (LimitMessage limitMessage in e)
            {
                Console.WriteLine($"limit:{limitMessage.Name};{limitMessage.Min};{limitMessage.Exposure};{limitMessage.Max}");
            }
        }
    }
}
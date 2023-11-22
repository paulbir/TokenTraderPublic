using System;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SharedTools;
using SharedTools.Interfaces;
using TokenTrader.State;

namespace TokenTrader.TradeModels
{
    static class TradeModelHelpers
    {
        public static bool TryRestartConnectorOnBrokenData(BookMessage             bookMessage,
                                                                       PricesState             state,
                                                                       IDataConnector          connector,
                                                                       ILogger                 logger,
                                                                       AsyncLock               tradeDataLocker,
                                                                       CancellationTokenSource childSource,
                                                                       int                     reconnectOnBrokenBookTimeoutSec,
                                                                       int                     waitingTimeoutMs)
        {
            //лочим, чтобы случайно не выставить заявку в остановленный коннектор, если стакан получается в торговом коннекторе
            try
            {
                if (!state.ArePricesReady)
                {
                    //если стакан сломался, то запоминаем, когда началось (кросс при включённом сеттинге CheckBookCross, бид и аск > 0, не широкий спрэд)
                    state.SetBookBrokenStartedTimestamp();

                    //если сломался давно и за время таймаута не починился, то переподключаемся
                    double brokenBookLastsSec = (DateTime.Now - state.BookBrokenStartedTimestamp).TotalSeconds;
                    if (brokenBookLastsSec >= reconnectOnBrokenBookTimeoutSec)
                    {
                        using (tradeDataLocker.Lock(childSource.NewPairedTimeoutToken(waitingTimeoutMs)))
                        {
                            logger.Enqueue($"{connector.ExchangeName}_{bookMessage.Isin} book is not ready: bid={state.Book.BestBid};ask={state.Book.BestAsk}." +
                                           $" Stopping connector {connector.Name}.");
                            connector.Stop();
                        }
                    }
                    else
                        logger.Enqueue($"{connector.ExchangeName}_{bookMessage.Isin} book is not ready: bid={state.Book.BestBid};ask={state.Book.BestAsk}. " +
                                       $"{(int)(reconnectOnBrokenBookTimeoutSec - brokenBookLastsSec)} seconds left till connector {connector.Name} reconnection.");

                    return true;
                }

                state.ResetBookBrokenStartedTimestamp();
            }
            catch (OperationCanceledException)
            {
                logger.Enqueue("Error: Connector_BookUpdateArrived async lock was canceled upon timeout.");
                return true;
            }

            return false;
        }

        public static bool IsStuckLongToStop(PricesState state,
                                             bool        arePricesNew,
                                             string      fullIsin,
                                             ILogger     logger,
                                             int         stopOnStuckBookTimeoutSec)
        {
            if (!arePricesNew)
            {
                //если стакан залип, то запоминаем, когда началось
                state.SetBookStuckStartedTimestamp();

                //если залип давно и за время таймаута не починился
                double stuckBookLastsSec = (DateTime.Now - state.BookStuckStartedTimestamp).TotalSeconds;
                if (stuckBookLastsSec >= stopOnStuckBookTimeoutSec)
                {
                    logger.Enqueue($"{fullIsin} book is stuck: bid={state.Book.BestBid};ask={state.Book.BestAsk} " +
                                   $"for {stuckBookLastsSec} seconds already.");
                    return true;
                }
                
                if (stuckBookLastsSec >= stopOnStuckBookTimeoutSec * 0.7)
                {
                    logger.Enqueue($"{fullIsin} book is stuck: bid={state.Book.BestBid};ask={state.Book.BestAsk}. " +
                                   $"{(int)(stopOnStuckBookTimeoutSec - stuckBookLastsSec)} seconds left till stop.");
                }

                return false;
            }

            state.ResetBookStuckStartedTimestamp();

            return false;
        }

        public static decimal QtyFromCurrencyVolume(bool isMarginMarket, bool isReverse, decimal volume, decimal isinMid, decimal lotSize)
        {
            if (isMarginMarket)
            {
                if (isReverse) return volume / lotSize;
                return volume / (isinMid * lotSize);
            }

            return volume / isinMid;
        }

        public static decimal QtyFromCurrencyFiatVolume(bool    isMarginMarket,
                                                        bool    isReverse,
                                                        decimal fiatVolume,
                                                        decimal isinMid,
                                                        decimal convertToFiatMid,
                                                        decimal lotSize)
        {
            if (isMarginMarket)
            {
                //TODO: Сейчас это работает только для контрактов, котирующихся к USDT, поэтому не надо пересчитывать в фиат
                if (isReverse) return fiatVolume / lotSize;
                return fiatVolume / (isinMid * lotSize);
            }

            return fiatVolume / (convertToFiatMid * isinMid);
        }

        public static decimal FiatVolumeFromQty(bool isMarginMarket, bool isReverse, decimal price, decimal qty, decimal convertToFiatMid, decimal lotSize)
        {
            if (isMarginMarket)
            {
                //TODO: Сейчас это работает только для контрактов, котирующихся к USDT, поэтому не надо пересчитывать в фиат
                if (isReverse) return qty * lotSize;
                return qty * lotSize * price;
            }

            return qty * price * convertToFiatMid;
        }

        public static decimal MarginVolumeFromQty(bool      isMarginMarket,
                                                  bool      isReverse,
                                                  decimal   price,
                                                  decimal   qty,
                                                  decimal   convertToFiatMid,
                                                  decimal   lotSize,
                                                  decimal   leverage,
                                                  OrderSide side)

        {
            if (isMarginMarket)
            {
                //TODO: здесь костыль. convertToFiatMid выступает в качестве цены для пересчёта в валюту маржи
                if (isReverse) return qty * lotSize / (convertToFiatMid * leverage);
                return qty                          * lotSize * price / (convertToFiatMid * leverage);
            }

            if (side == OrderSide.Buy) return qty * price;
            return qty;
        }

        public static bool IsEnoughBalance(bool      isMarginMarket,
                                           bool      isReverse,
                                           decimal   price,
                                           decimal   qty,
                                           decimal   convertToFiatMid,
                                           decimal   lotSize,
                                           decimal   leverage,
                                           OrderSide side,
                                           decimal   balance)
        {
            return MarginVolumeFromQty(isMarginMarket, isReverse, price, qty, convertToFiatMid, lotSize, leverage, side) < balance;
        }
    }
}
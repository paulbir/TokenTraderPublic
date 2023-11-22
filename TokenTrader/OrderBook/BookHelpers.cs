using System;
using SharedDataStructures;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Messages;
using SharedTools.Interfaces;

namespace TokenTrader.OrderBook
{
    static class BookHelpers
    {
        public static void ApplySnapshot(BookMessage              bookMessage,
                                         UnlimitedOrderBook<long> book,
                                         int                      numBookErrorsInWindowToThrow,
                                         bool                     checkBookCross = false,
                                         ILogger                  logger         = null)
        {
            book.Clear();
            foreach (PriceLevel bid in bookMessage.Bids)
            {
                if (bid.ApplyMethod == PriceLevelApplyMethod.OrderLog)
                    book.InsertDeleteOrUpdateQty(OrderSide.Buy, bid.Price, bid.Qty, bid.Id); //для коннекторов, где фид ордерлогоподобный
                else book.Insert(OrderSide.Buy, bid.Price, bid.Qty, bid.Id);
            }

            foreach (PriceLevel ask in bookMessage.Asks)
            {
                if (ask.ApplyMethod == PriceLevelApplyMethod.OrderLog)
                    book.InsertDeleteOrUpdateQty(OrderSide.Sell, ask.Price, ask.Qty, ask.Id); //для коннекторов, где фид ордерлогоподобный
                else book.Insert(OrderSide.Sell, ask.Price, ask.Qty, ask.Id);
            }

            //if (book.ErrorsTimeQ.Count >= numBookErrorsInWindowToThrow)
            //    throw new OrderBookBrokenException($"Got {numBookErrorsInWindowToThrow} {bookMessage.Isin} order book " +
            //                                       $"snapshot errors for last {book.ErrorsTimeQ.WindowMs / 1000}sec.");
        }

        public static void ApplyUpdate(BookMessage              bookMessage,
                                       UnlimitedOrderBook<long> book,
                                       int                      numBookErrorsInWindowToThrow,
                                       bool                     checkBookCross,
                                       ILogger                  logger)
        {
            decimal bestBid        = book.BestBid;
            decimal bestAsk        = book.BestAsk;
            bool    anyBuyCrossed  = false;
            bool    anySellCrossed = false;

            foreach (PriceLevel bid in bookMessage.Bids)
            {
                ApplyPriceLevelUpdate(OrderSide.Buy, bid, book, bestBid, bestAsk, logger, out bool isCrossed);
                anyBuyCrossed |= isCrossed;
            }

            foreach (PriceLevel ask in bookMessage.Asks)
            {
                ApplyPriceLevelUpdate(OrderSide.Sell, ask, book, bestBid, bestAsk, logger, out bool isCrossed);
                anySellCrossed |= isCrossed;
            }

            if (!checkBookCross) return; //есть OTC стаканы, где бид с аском могут перекрещиваться. там не делаем проверки на кросс и не мэтчим.

            //механизм очистки залипших сверху заявок. если залипли, то мэтчим ту сторону, на которой не было апдейта
            if (anyBuyCrossed && anySellCrossed)
                throw new OrderBookBrokenException($"Got book updates message:\n{bookMessage}.\nBefore updating bestBid={bestBid};bestAsk={bestAsk}. " +
                                                   "But got both buyCrossed and sellCrossed. Can\'t choose which side to match.");

            //GetHashCode нужен для того, чтобы поудалять из ordersById. возможно в этом стакане другие id, но это попытка
            if (anyBuyCrossed) book.MatchDownToPrice(OrderSide.Sell,      book.BestBid, logger);
            else if (anySellCrossed) book.MatchDownToPrice(OrderSide.Buy, book.BestAsk, logger);

            if (book.ErrorsTimeQ.Count >= numBookErrorsInWindowToThrow)
            {
                book.ErrorsTimeQ.Clear();
                throw new OrderBookBrokenException($"Got {numBookErrorsInWindowToThrow} {bookMessage.Isin} order book " +
                                                   $"update errors for last {book.ErrorsTimeQ.WindowMs / 1000}sec.");
            }
        }

        static void ApplyPriceLevelUpdate(OrderSide                side,
                                          PriceLevel               priceLevel,
                                          UnlimitedOrderBook<long> book,
                                          decimal                  lastBestBid,
                                          decimal                  lastBestAsk,
                                          ILogger                  logger,
                                          out bool                 isCrossed)
        {
            switch (priceLevel.ApplyMethod)
            {
                case PriceLevelApplyMethod.Straight:
                    if (priceLevel.Qty == 0) book.Delete(side, priceLevel.Id, logger);
                    else book.InsertOrUpdate(side, priceLevel.Price, priceLevel.Qty, priceLevel.Id);
                    break;

                //для коннекторов, где снэпшот агрегированный, а инкременты по каждой заявке отдельно. или весь фид ордерлогоподобный
                case PriceLevelApplyMethod.OrderLog:
                    book.InsertDeleteOrUpdateQty(side, priceLevel.Price, priceLevel.Qty, priceLevel.Id);
                    break;

                //для странных коннекторов, где есть команда удалить все уровни перед указанной ценой. теперь эта цены - best
                case PriceLevelApplyMethod.DeleteAheadPrice:
                    if (priceLevel.Price == 0) book.ClearOneSide(side); //стакан опустел с одной стороны
                    else
                    {
                        book.DeleteLevelsAheadPrice(side, priceLevel.Price, logger);
                        book.Update(side, priceLevel.Qty, priceLevel.Id, logger); //уровень на указанной цене надо обновить на объём из апдейта
                    }

                    break;
                default: throw new ArgumentOutOfRangeException($"Unknown priceLevel.ApplyMethod at {priceLevel}.");
            }

            //для механизма очистки залипших сверху заявок. если залипли, то мэтчим ту сторону, на которой не было апдейта.
            isCrossed = false;
            if (priceLevel.Price == 0) return;
            if (side      == OrderSide.Buy  && lastBestAsk > 0 && priceLevel.Qty > 0) isCrossed = priceLevel.Price >= lastBestAsk;
            else if (side == OrderSide.Sell && lastBestBid > 0 && priceLevel.Qty > 0) isCrossed = priceLevel.Price <= lastBestBid;
        }

        public static bool ArePricesNew(BaseOrderBook<long> book, bool setPrevValues, bool useVwap = false, decimal vwapQty = 0)
        {
            if (useVwap && vwapQty > 0)
            {
                decimal bidVwap = book.GetOneSideVwap(OrderSide.Buy,  vwapQty);
                decimal askVwap = book.GetOneSideVwap(OrderSide.Sell, vwapQty);
                if (bidVwap != book.PrevSentVwapBid || askVwap != book.PrevSentVwapAsk)
                {
                    if (setPrevValues) book.SetPrevSentVwap(bidVwap, askVwap);
                    return true;
                }
            }

            if (book.BestBid != book.PrevSentBestBid || book.BestAsk != book.PrevSentBestAsk)
            {
                if (setPrevValues) book.SetPrevSentBest();
                return true;
            }

            return false;
        }
    }
}
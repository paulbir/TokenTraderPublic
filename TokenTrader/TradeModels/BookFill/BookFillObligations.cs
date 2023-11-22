using System;
using System.Collections.Generic;
using SharedDataStructures.Messages;
using SharedTools;
using TokenTrader.Initialization;
using TokenTrader.State;

namespace TokenTrader.TradeModels.BookFill
{
    partial class BookFill
    {
        void ProcessObligations(IsinMMState           mmState,
                                BookFillIsinParams    isinParams,
                                string                changedTradeIsin,
                                decimal               minPredictorsBid,
                                decimal               maxPredictorsAsk,
                                UsedAddOrderPriceData usedPricesData,
                                decimal               marginPosition)
        {
            //удаляем повисшие заявки, если прошёл интервал
            if (mmState.DelayToNextOrdersActionPassed)
            {
                List<OrderMessage> lostOrders = mmState.GetLostObligationOrders();
                CancelOrders(lostOrders, mmState, isinParams.Isin);
            }

            decimal mid                = mmState.GetBaseMid();
            bool    shouldProcessBuys  = ShouldProcess(mmState, isinParams, OrderSide.Buy,  minPredictorsBid, mid, out decimal bestActiveBid);
            bool    shouldProcessSells = ShouldProcess(mmState, isinParams, OrderSide.Sell, maxPredictorsAsk, mid, out decimal bestActiveAsk);

            if (shouldProcessBuys) CancelObligations(mmState,  changedTradeIsin, OrderSide.Buy);
            if (shouldProcessSells) CancelObligations(mmState, changedTradeIsin, OrderSide.Sell);

            if (shouldProcessBuys)
                TryAddObligations(mmState, isinParams, changedTradeIsin, OrderSide.Buy, minPredictorsBid, marginPosition, mid, bestActiveAsk, usedPricesData);
            if (shouldProcessSells)
                TryAddObligations(mmState, isinParams, changedTradeIsin, OrderSide.Sell, maxPredictorsAsk, marginPosition, mid, bestActiveBid, usedPricesData);

            //ProcessObligationsIsinOneSide(mmState, isinParams, changedTradeIsin, OrderSide.Buy,  minPredictorsBid, marginPosition, usedPricesData);
            //ProcessObligationsIsinOneSide(mmState, isinParams, changedTradeIsin, OrderSide.Sell, maxPredictorsAsk, marginPosition, usedPricesData);
        }

        //void ProcessObligationsIsinOneSide(IsinMMState           mmState,
        //                                   BookFillIsinParams    isinParams,
        //                                   string                isin,
        //                                   OrderSide             side,
        //                                   decimal               predictorBest,
        //                                   decimal               marginPosition,
        //                                   UsedAddOrderPriceData usedPricesData)
        //{

        //    decimal mid = mmState.GetBaseMid();

        //    if (!ShouldProcess(mmState, isinParams, side, predictorBest, mid)) return;

        //    //(List<string> orderIdsToCancel, List<OrderState> obligationsToAdd) = mmState.GetObligationsToModify(side);

        //    CancelObligations(mmState, isin, side);

        //    TryAddObligations(mmState, isinParams, isin, side, predictorBest, marginPosition, usedPricesData, mid);
        //}

        static bool ShouldProcess(IsinMMState        mmState,
                                  BookFillIsinParams isinParams,
                                  OrderSide          side,
                                  decimal            predictorBest,
                                  decimal            mid,
                                  out decimal        bestActivePrice)
        {
            bestActivePrice = mmState.GetBestActivePrice(side);

            //если прошёл таймаут, то сразу переходим к действию. расстояние можно не проверять. иначе проверяем.
            if (mmState.DelayToNextOrdersActionPassed) return true;

            decimal orderToPredictorDistance         = Math.Abs(bestActivePrice - predictorBest);
            decimal minSpread                        = isinParams.MinObligationSpreadPerc / 100       * mid;
            decimal bestObligationSpreadMismatchFrac = Math.Abs(orderToPredictorDistance - minSpread) / minSpread;

            //и спрэд до предиктора в пределах допустимого. можно ничего не двигать.
            return bestObligationSpreadMismatchFrac > isinParams.BestObligationSpreadTolerancePerc / 100;
        }

        void CancelObligations(IsinMMState mmState, string isin, OrderSide side)
        {
            List<string> orderIdsToCancel = mmState.GetObligationsToCancel(side);
            foreach (string orderId in orderIdsToCancel) CancelSingleOrder(isin, side, orderId, mmState, true, orderId);
        }

        void TryAddObligations(IsinMMState           mmState,
                               BookFillIsinParams    isinParams,
                               string                isin,
                               OrderSide             side,
                               decimal               predictorBest,
                               decimal               marginPosition,
                               decimal               mid,
                               decimal               bestActiveOppositePrice,
                               UsedAddOrderPriceData usedPricesData)
        {
            if (mmState.IsPotentialOneSideLimitExceeded(side, out decimal activeVol))
            {
                logger.Enqueue($"Potential position limit exceeded on side {side}. Position={mmState.PositionFiat}. ActiveVol={activeVol}. So skip adding for isin {isin}.");
                return;
            }

            List<OrderState> obligationsToAdd    = mmState.GetObligationsToAdd(side);
            decimal          conversionToFiatMid = (mmState.ConversionToFiatPricesState.Book.BestBid + mmState.ConversionToFiatPricesState.Book.BestAsk) / 2;


            //если пока ещё ничего не выставляли, то в OrderStates пусто. проходимся по всем обязательствам. они будут добавлены.
            if (mmState.IsOrderStatesEmpty(side))
            {
                foreach (Obligation obligation in isinParams.Obligations)
                    TryAddObligationOrder(mmState,
                                          isinParams,
                                          side,
                                          obligation.SpreadOneSidePerc,
                                          obligation.VolumeOneSideFiat,
                                          predictorBest,
                                          marginPosition,
                                          mid,
                                          conversionToFiatMid,
                                          bestActiveOppositePrice,
                                          usedPricesData);
            }
            else //иначе всё уже было добавлено ранее и имеет какой-то статус. какие-то обязательства возможно уже нужно выставлять.
            {
                foreach (OrderState obligationState in obligationsToAdd)
                    TryAddObligationOrder(mmState,
                                          isinParams,
                                          side,
                                          obligationState.SpreadOneSidePerc,
                                          obligationState.VolumeOneSideFiat,
                                          predictorBest,
                                          marginPosition,
                                          mid,
                                          conversionToFiatMid,
                                          bestActiveOppositePrice,
                                          usedPricesData);
            }
        }

        void TryAddObligationOrder(IsinMMState           mmState,
                                   BookFillIsinParams    isinParams,
                                   OrderSide             side,
                                   decimal               spreadOneSidePerc,
                                   decimal               volumeOneSideFiat,
                                   decimal               predictorBest,
                                   decimal               marginPosition,
                                   decimal               mid,
                                   decimal               conversionToFiatMid,
                                   decimal               bestActiveOppositePrice,
                                   UsedAddOrderPriceData usedPricesData)
        {
            string  isin       = isinParams.Isin;
            decimal spread     = spreadOneSidePerc / 100 * mid;
            decimal orderPrice = side == OrderSide.Buy ? predictorBest - spread : predictorBest + spread;
            orderPrice = Math.Round(orderPrice / isinParams.MinStep) * isinParams.MinStep;

            if (IsPriceCrossed(orderPrice, bestActiveOppositePrice, side))
            {
                logger.Enqueue($"Skip adding obligation for {isin} side={side};spread={spread};vol={volumeOneSideFiat}. " +
                               $"price={orderPrice} has crossed oppositePrice={bestActiveOppositePrice}.");
                return;
            }

            decimal orderQty = TradeModelHelpers.QtyFromCurrencyFiatVolume(context.IsMarginMarket,
                                                                           isinParams.IsReverse.Value,
                                                                           volumeOneSideFiat,
                                                                           mid,
                                                                           conversionToFiatMid,
                                                                           isinParams.LotSize);
            orderQty = Math.Round(orderQty / isinParams.MinQty) * isinParams.MinQty;

            if (!IsEnoughBalance(mmState, isinParams, isin, side, orderQty, orderPrice, "ObligationAdd", marginPosition)) return;

            string clientOrderId = CreateClientOrderId(isin, side);

            if (!run) return;

            logger.Enqueue($"ObligationAdd side={side};spread={spread}.vol={volumeOneSideFiat}. " +
                           $"Going to add {isin}|price={orderPrice}|qty={orderQty}|{clientOrderId}.");

            if (context.UseUdp) usedPricesDatas.TryAdd(clientOrderId, usedPricesData);

            //нужно обновить до отправки заявки, потому что отправка заявки может быть синхронная, через rest. и NewOrderAdded вызовется до AddOrUpdateLocalOrderState.
            mmState.AddOrUpdateLocalOrderState(spreadOneSidePerc, clientOrderId, LocalOrderStatus.AddPending, side, volumeOneSideFiat);

            mmState.TradeConnector.AddOrder(clientOrderId, isin, side, orderPrice, orderQty, requestIdGenerator.Id);
        }

        bool IsPriceCrossed(decimal price, decimal bestActiveOppositePrice, OrderSide side)
        {
            if (bestActiveOppositePrice <= 0) return false;

            if (side     == OrderSide.Buy) return price >= bestActiveOppositePrice;
            return price <= bestActiveOppositePrice;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using SharedDataStructures.Messages;
using SharedTools;
using TokenTrader.Initialization;
using TokenTrader.State;

namespace TokenTrader.TradeModels.BookFill
{
    partial class BookFill
    {
        void ProcessRandomFill(IsinMMState        mmState,
                               BookFillIsinParams isinParams,
                               string             changedTradeIsin,
                               decimal            minPredictorsBid,
                               decimal            marginPosition,
                               decimal            maxPredictorsAsk)
        {
            ProcessRandomFillIsinOneSide(mmState,
                               isinParams,
                               changedTradeIsin,
                               OrderSide.Buy,
                               minPredictorsBid - isinParams.RandomFill.MinSpreadOneSideMinsteps * isinParams.MinStep,
                               marginPosition);
            ProcessRandomFillIsinOneSide(mmState,
                               isinParams,
                               changedTradeIsin,
                               OrderSide.Sell,
                               maxPredictorsAsk + isinParams.RandomFill.MinSpreadOneSideMinsteps * isinParams.MinStep,
                               marginPosition);
        }

        void ProcessRandomFillIsinOneSide(IsinMMState mmState, BookFillIsinParams isinParams, string isin, OrderSide side, decimal predictorBest, decimal marginPosition)
        {
            CancelFront(mmState, isin, predictorBest, side);

            //сразу только удаляем ближайшие заявки. всё дальнее раз в интервал.
            if (!mmState.DelayToNextOrdersActionPassed) return;

            CancelOneBack(mmState, isinParams, isin, side, predictorBest);

            bool potentialLimitExceeded = mmState.IsPotentialOneSideLimitExceeded(side, out decimal activeVol);

            if (isinParams.RandomFill.ChangeDeepOrders.Value && //флаг
                (activeVol >= isinParams.RandomFill.VolumeOneSideFiat * FullVolToleranceCoef ||
                 potentialLimitExceeded
                ) &&                                                                                                    //начинаем менять заявки в глубине, только когда всё заполнилось
                TryPrepareSubstDeepOrder(mmState, isinParams, isin, side, predictorBest, out OrderMessage randomOrder)) //всё нормально выбралось
                //снимаем заявку. после полученя подтверждения о снятии, выставится новая.
                CancelSingleOrder(isin, side, randomOrder.OrderId, mmState, true, randomOrder.ToString());

            if (potentialLimitExceeded)
            {
                logger.Enqueue($"Potential position limit exceeded on side {side}. Position={mmState.PositionFiat}. ActiveVol={activeVol}. So skip adding for isin {isin}.");
                return;
            }

            logger.Enqueue($"Position={mmState.PositionFiat};ActiveVol={activeVol} for isin {isin}.");

            AddOneFront(mmState, isinParams, isin, side, predictorBest, marginPosition, out bool isTooCloseToPredictor);
            AddOneBack(mmState, isinParams, isin, side, predictorBest, marginPosition, out bool isTooCloseToWindowBack);

            if (isTooCloseToPredictor && isTooCloseToWindowBack && activeVol < isinParams.RandomFill.VolumeOneSideFiat * FullVolToleranceCoef)
                AddOneDeep(mmState, isinParams, isin, side, marginPosition);
        }

        void AddOneFront(IsinMMState        mmState,
                         BookFillIsinParams isinParams,
                         string             isin,
                         OrderSide          side,
                         decimal            predictorBest,
                         decimal            marginPosition,
                         out bool           isTooCloseToPredictor)
        {
            isTooCloseToPredictor = false;
            decimal bestActivePrice = mmState.GetBestActivePrice(side);

            //у нас нет активных заявок. не перед чем выcтавлять.
            if (bestActivePrice <= 0) return;

            decimal orderToPredictorDistance         = side == OrderSide.Buy ? predictorBest - bestActivePrice : bestActivePrice - predictorBest;
            int     orderToPredictorDistanceMinsteps = (int)Math.Round(orderToPredictorDistance / isinParams.MinStep);

            //наша активная цена либо перед предиктором, либо за ним, но слишком близко.
            //ничего дополнительно выставлять не нужно.
            if (orderToPredictorDistanceMinsteps <= isinParams.RandomFill.OrdersDistanceMuMinsteps / OrderToEdgeDistanceToleranceCoef)
            {
                //logger.Enqueue($"AddOneFront. bestActivePrice={bestActivePrice};orderToPredictorDistanceMinsteps={orderToPredictorDistanceMinsteps} <= " +
                //               $"ordersDistanceMinstepsMu*tolerance={isinParams.OrdersDistanceMuMinsteps * OrderToPredictorDistanceToleranceCoef} " +
                //               $" on side={side}. Skip this order adding for isin {isin}.");
                isTooCloseToPredictor = true;
                return;
            }

            if (!TryCalcQty(mmState, isinParams, isin, "AddFront", side, out decimal orderQty)) return;
            if (!TryCalcDistance(isinParams, isin, "AddFront", side, out decimal orderDistance)) return;

            decimal orderPrice = side == OrderSide.Buy ? bestActivePrice + orderDistance : bestActivePrice - orderDistance;
            orderPrice = Math.Round(orderPrice / isinParams.MinStep) * isinParams.MinStep;

            if (!IsEnoughBalance(mmState, isinParams, isin, side, orderQty, orderPrice, "AddOneFront", marginPosition)) return;

            if (IsInFrontPredictor(isin, side, predictorBest, orderPrice, "AddOneFront")) return;

            string clientOrderId = CreateClientOrderId(isin, side);

            logger.Enqueue($"AddOneFront for side {side}. Best active price={bestActivePrice}. Predictor best={predictorBest} including spread. " +
                           $"Going to add {isin}|price={orderPrice}|qty={orderQty}|{clientOrderId} for isin {isin}.");

            mmState.TradeConnector.AddOrder(clientOrderId, isin, side, orderPrice, orderQty, requestIdGenerator.Id);
        }

        void AddOneBack(IsinMMState        mmState,
                        BookFillIsinParams isinParams,
                        string             isin,
                        OrderSide          side,
                        decimal            predictorBest,
                        decimal            marginPosition,
                        out bool           isTooCloseToWindowBack)
        {
            isTooCloseToWindowBack = false;
            decimal windowFarPrice = CalcWindowFarPrice(isinParams, side, predictorBest);

            decimal farthestActivePrice              = mmState.GetFarthestActivePrice(side);
            decimal orderToWindowFarDistance         = side == OrderSide.Buy ? farthestActivePrice - windowFarPrice : windowFarPrice - farthestActivePrice;
            int     orderToWindowFarDistanceMinsteps = (int)Math.Round(orderToWindowFarDistance / isinParams.MinStep);

            //дальняя заявка слишком близко к крайней цене. поэтому за ней ничего не выставляем.
            if (orderToWindowFarDistanceMinsteps <= isinParams.RandomFill.OrdersDistanceMuMinsteps / OrderToEdgeDistanceToleranceCoef)
            {
                //logger.Enqueue($"AddOneBack. orderToWindowFarDistanceMinsteps={orderToWindowFarDistanceMinsteps} <= " +
                //               $"ordersDistanceMinstepsMu*tolerance={isinParams.OrdersDistanceMuMinsteps * OrderToPredictorDistanceToleranceCoef} " +
                //               $" on side={side}. Skip this order adding for isin {isin}.");
                isTooCloseToWindowBack = true;
                return;
            }

            if (!TryCalcQty(mmState, isinParams, isin, "AddOneBack", side, out decimal orderQty)) return;
            if (!TryCalcDistance(isinParams, isin, "AddOneBack", side, out decimal orderDistance)) return;

            decimal orderPrice = side == OrderSide.Buy ? farthestActivePrice - orderDistance : farthestActivePrice + orderDistance;
            orderPrice = Math.Round(orderPrice / isinParams.MinStep) * isinParams.MinStep;

            if (IsBehindWindowFarPrice(isin, side, orderPrice, windowFarPrice, "AddOneBack")) return;

            if (!IsEnoughBalance(mmState, isinParams, isin, side, orderQty, orderPrice, "AddOneBack", marginPosition)) return;

            string clientOrderId = CreateClientOrderId(isin, side);
            logger.Enqueue($"AddOneBack for side {side}. Farthest active price={farthestActivePrice}. Far window price={windowFarPrice}. " +
                           $"Going to add {isin}|price={orderPrice}|qty={orderQty}|{clientOrderId}.");

            mmState.TradeConnector.AddOrder(clientOrderId, isin, side, orderPrice, orderQty, requestIdGenerator.Id);
        }

        bool TryPrepareSubstDeepOrder(IsinMMState        mmState,
                                      BookFillIsinParams isinParams,
                                      string             isin,
                                      OrderSide          side,
                                      decimal            predictorBest,
                                      out OrderMessage   randomOrder)
        {
            if (!mmState.TryGetRandomActiveOrder(side, out randomOrder))
            {
                logger.Enqueue($"TryPrepareSubstDeepOrder for side {side}. Couldn't get random active order. Skip randomizing order for isin {isin}.");
                return false;
            }

            decimal newOrderPrice = randomOrder.Price;

            decimal orderDistance = 0;
            bool    changePrice   = ThreadSafeRandom.ThisThreadsRandom.Next50PercentChoice();
            if (changePrice)
            {
                if (!TryCalcDistance(isinParams, isin, "TryPrepareSubstDeepOrder", side, out orderDistance)) return false;
                orderDistance *= ChangedOrderPriceRegionShrinkCoef;
                int shiftSide = ThreadSafeRandom.ThisThreadsRandom.Next50PercentChoice() ? -1 : 1;
                newOrderPrice += orderDistance                                  * shiftSide;
                newOrderPrice =  Math.Round(newOrderPrice / isinParams.MinStep) * isinParams.MinStep;
            }

            if (!TryCalcQty(mmState, isinParams, isin, "TryPrepareSubstDeepOrder", side, out decimal newOrderQty)) return false;
            if (IsInFrontPredictor(isin, side, predictorBest, newOrderPrice, "TryPrepareSubstDeepOrder")) return false;

            decimal windowFarPrice = CalcWindowFarPrice(isinParams, side, predictorBest);
            if (IsBehindWindowFarPrice(isin, side, newOrderPrice, windowFarPrice, "TryPrepareSubstDeepOrder")) return false;

            preparedRandomizeOrdersByCancelId.TryAdd(randomOrder.OrderId, (newOrderPrice, newOrderQty));
            logger.Enqueue($"Prepared substitute for deep order for side {side}. Far window price={windowFarPrice}. Predictor best={predictorBest} including spread. " +
                           $"Order to cancel: {randomOrder}. New price={newOrderPrice} using shrinked orderDistance={orderDistance}. New qty={newOrderQty}.");
            return true;
        }

        void AddPreparedRandomOrderOnCancel(IsinMMState mmState, decimal price, decimal qty, OrderMessage canceledOrder)
        {
            string    isin          = canceledOrder.Isin;
            OrderSide side          = canceledOrder.Side;
            string    clientOrderId = CreateClientOrderId(isin, side);

            logger.Enqueue($"AddPreparedRandomOrderOnCancel for side {side}. Canceled order:{canceledOrder}. " +
                           $"Going to add {isin}|price={price}|qty={qty}|{clientOrderId}.");

            mmState.TradeConnector.AddOrder(clientOrderId, isin, side, price, qty, requestIdGenerator.Id);
        }

        void AddOneDeep(IsinMMState mmState, BookFillIsinParams isinParams, string isin, OrderSide side, decimal marginPosition)
        {
            if (!TryCalcQty(mmState, isinParams, isin, "AddOneDeep", side, out decimal orderQty)) return;
            if (!mmState.TryGetActivePriceLevelsWithMaxPriceDifference(side, out decimal firstPrice, out decimal secondPrice))
            {
                logger.Enqueue($"AddOneDeep for side {side}. Couldn't get price levels with max difference. " +
                               $"Got firstPrice={firstPrice};secondPrice={secondPrice}. Skip this order adding for isin {isin}.");
                return;
            }

            decimal orderPrice = (secondPrice + firstPrice) / 2;
            orderPrice = Math.Round(orderPrice / isinParams.MinStep) * isinParams.MinStep;

            if (!IsEnoughBalance(mmState, isinParams, isin, side, orderQty, orderPrice, "AddOneDeep", marginPosition)) return;

            string clientOrderId = CreateClientOrderId(isin, side);
            logger.Enqueue($"AddOneDeep for side {side}. Active prices with max difference: firstPrice={firstPrice};secondPrice={secondPrice}. " +
                           $"Going to add {isin}|price={orderPrice}|qty={orderQty}|{clientOrderId}.");

            mmState.TradeConnector.AddOrder(clientOrderId, isin, side, orderPrice, orderQty, requestIdGenerator.Id);
        }

        bool TryCalcQty(IsinMMState mmState, BookFillIsinParams isinParams, string isin, string method, OrderSide side, out decimal orderQty)
        {
            decimal conversionToFiatMid = (mmState.ConversionToFiatPricesState.Book.BestBid + mmState.ConversionToFiatPricesState.Book.BestAsk) / 2;
            decimal mid                 = mmState.GetBaseMid();
            decimal minOrderQty = TradeModelHelpers.QtyFromCurrencyVolume(context.IsMarginMarket,
                                                                          isinParams.IsReverse.Value,
                                                                          isinParams.MinOrderVolume,
                                                                          mid,
                                                                          isinParams.LotSize);

            double orderQtyMu = (double)TradeModelHelpers.QtyFromCurrencyFiatVolume(context.IsMarginMarket,
                                                                                    isinParams.IsReverse.Value,
                                                                                    isinParams.RandomFill.OrderVolumeMuFiat,
                                                                                    mid,
                                                                                    conversionToFiatMid,
                                                                                    isinParams.LotSize);
            double orderQtySigma = orderQtyMu * (double)isinParams.RandomFill.OrderQtySigmaFrac;
            orderQty = (decimal)ThreadSafeRandom.ThisThreadsRandom.NextGaussian(orderQtyMu, orderQtySigma);
            orderQty = Math.Max(orderQty, minOrderQty); //количество в заявке не может быть меньше минимального объёма

            orderQty = Math.Round(orderQty / isinParams.MinQty) * isinParams.MinQty;
            if (orderQty <= 0)
            {
                logger.Enqueue($"{method}. Chose orderQty={orderQty} <= 0  on side={side}. Skip this order adding for isin {isin}.");
                return false;
            }

            return true;
        }

        bool TryCalcDistance(BookFillIsinParams isinParams, string isin, string method, OrderSide side, out decimal orderDistance)
        {
            double orderDistanceMinstepsSigma = isinParams.RandomFill.OrdersDistanceMuMinsteps * OrderDistanceMinstepsSigmaFrac;
            orderDistance =
                (decimal)ThreadSafeRandom.ThisThreadsRandom.NextGaussian(isinParams.RandomFill.OrdersDistanceMuMinsteps, orderDistanceMinstepsSigma) *
                isinParams.MinStep;

            if (orderDistance <= 0)
            {
                logger.Enqueue($"{method}. Chose orderDistance={orderDistance} <= 0  on side={side}. Skip this order adding for isin {isin}.");
                return false;
            }

            return true;
        }

        static decimal CalcWindowFarPrice(BookFillIsinParams isinParams, OrderSide side, decimal predictorBest)
        {
            int numOrdersOneSide          = isinParams.RandomFill.VolumeOneSideFiat / isinParams.RandomFill.OrderVolumeMuFiat;
            int ordersWindowWidthMinsteps = numOrdersOneSide                        * isinParams.RandomFill.OrdersDistanceMuMinsteps;
            decimal windowFarPrice = side == OrderSide.Buy
                                         ? predictorBest - ordersWindowWidthMinsteps * isinParams.MinStep
                                         : predictorBest + ordersWindowWidthMinsteps * isinParams.MinStep;
            return Math.Max(windowFarPrice, isinParams.MinStep);
        }

        bool IsInFrontPredictor(string isin, OrderSide side, decimal predictorBest, decimal orderPrice, string method)
        {
            if (side == OrderSide.Buy && orderPrice >= predictorBest || side == OrderSide.Sell && orderPrice <= predictorBest)
            {
                logger.Enqueue($"{method}. Chosen orderPrice={orderPrice} is in front of predictorbest={predictorBest} including spread on side={side}. " +
                               $"Skip this order adding for isin {isin}.");
                return true;
            }

            return false;
        }

        bool IsBehindWindowFarPrice(string isin, OrderSide side, decimal orderPrice, decimal windowFarPrice, string method)
        {
            if (side == OrderSide.Buy && orderPrice <= windowFarPrice || side == OrderSide.Sell && orderPrice >= windowFarPrice)
            {
                logger.Enqueue($"{method}. Chosen orderPrice={orderPrice} is behind windowFarPrice={windowFarPrice} on side={side}. " +
                               $"Skip this order adding for isin {isin}.");

                return true;
            }

            return false;
        }

        void CancelFront(IsinMMState mmState, string isin, decimal predictorBest, OrderSide side)
        {
            List<OrderMessage> ordersToCancel = mmState.GetActiveOrdersInFrontOfPrice(predictorBest, side);
            if (ordersToCancel != null)
            {
                logger.Enqueue($"CancelFront for side {side}. Predictor best={predictorBest} including spread for isin {isin}:");
                CancelOrders(ordersToCancel, mmState, isin);
            }
        }

        void CancelOneBack(IsinMMState mmState, BookFillIsinParams isinParams, string isin, OrderSide side, decimal predictorBest)
        {
            int numOrdersOneSide          = isinParams.RandomFill.VolumeOneSideFiat / isinParams.RandomFill.OrderVolumeMuFiat;
            int ordersWindowWidthMinsteps = numOrdersOneSide                        * isinParams.RandomFill.OrdersDistanceMuMinsteps;
            decimal windowFarPrice = side == OrderSide.Buy
                                         ? predictorBest - ordersWindowWidthMinsteps * isinParams.MinStep
                                         : predictorBest + ordersWindowWidthMinsteps * isinParams.MinStep;

            List<OrderMessage> ordersToCancel = mmState.GetActiveOrdersBehindPrice(windowFarPrice, side);

            //снимать за границей окна будем по одной заявке
            if (ordersToCancel != null)
            {
                logger.Enqueue($"CancelOneBack for side {side}. Predictor best={predictorBest} including spread. Far window price={windowFarPrice} for isin {isin}:");
                CancelOrders(ordersToCancel.Take(1).ToList(), mmState, isin);
            }
        }
    }
}
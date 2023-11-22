using System;
using SharedDataStructures;
using SharedDataStructures.Messages;

namespace CoinFlexConnector.Model
{
    class CoinFlexPriceLevel : PriceLevel
    {
        public CoinFlexPriceLevel(/*long id,*/ decimal priceUnscaled, decimal qtyUnscaled, decimal scale, bool isQtyIncreased)
        {
            //Id    = id;
            //отрицательная цена нужна, чтобы корректно обрабатывался ордерлог. потому что обычно агрегированный стакан. для ордерлога такой костыль.
            //Price = (isUpdate ? -1 : 1) * priceUnscaled / scale;
            //Price = -1 * priceUnscaled / scale;
            Price = priceUnscaled / scale;
            Qty   = (isQtyIncreased ? 1 : -1) * Math.Abs(qtyUnscaled) / scale;
            ApplyMethod = PriceLevelApplyMethod.OrderLog;
        }

        CoinFlexPriceLevel(decimal price, decimal qty, PriceLevelApplyMethod applyMethod)
        {
            Price = price;
            Qty = qty;
            ApplyMethod = applyMethod;
        }

        public CoinFlexPriceLevel DeepCopy() => new CoinFlexPriceLevel(Price, Qty, ApplyMethod);
        public CoinFlexPriceLevel DecreasingLevel() => new CoinFlexPriceLevel(Price, -1 * Math.Abs(Qty), ApplyMethod);

        public static CoinFlexPriceLevel CreateDiffLevel(CoinFlexPriceLevel newLevel, CoinFlexPriceLevel oldLevel) =>
            new CoinFlexPriceLevel(newLevel.Price, newLevel.Qty - oldLevel.Qty, oldLevel.ApplyMethod);

        public void UpdateQty(decimal qtyUnscaled, decimal scale, bool isQtyIncreased)
        {
            Qty = (isQtyIncreased ? 1 : -1) * Math.Abs(qtyUnscaled) / scale;
        }

    }
}
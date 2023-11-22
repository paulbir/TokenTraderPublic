using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Linq;
using SharedDataStructures.Messages;
using SharedTools;
using SharedTools.Interfaces;
using TokenTrader.DataStructures;

namespace TokenTrader.OrderBook
{
    public class UnlimitedOrderBook<TOrderId> : BaseOrderBook<TOrderId>
    {
        readonly object locker = new object();
        readonly int numLevelsToSend;
        readonly ConcurrentDictionary<TOrderId, Order<TOrderId>> bidsById = new ConcurrentDictionary<TOrderId, Order<TOrderId>>();
        readonly ConcurrentDictionary<TOrderId, Order<TOrderId>> asksById = new ConcurrentDictionary<TOrderId, Order<TOrderId>>();
        readonly List<decimal> removedWhileMatching = new List<decimal>();
        readonly bool useMatching;

        readonly SortedDictionary<decimal, Order<TOrderId>> bids = new SortedDictionary<decimal, Order<TOrderId>>(new ReverseComparer<decimal>());
        readonly SortedDictionary<decimal, Order<TOrderId>> asks = new SortedDictionary<decimal, Order<TOrderId>>();

        public override decimal BestBid 
        {
            get
            {
                lock(locker) return bids.Count == 0 ? 0 : bids.First().Value.Price;
            }
        }

        public override decimal BestAsk
        {
            get
            {
                lock (locker) return asks.Count == 0 ? 0 : asks.First().Value.Price;
            }
        }
        
        public override decimal BestBidQty
        {
            get
            {
                lock(locker) return bids.Count == 0 ? 0 : bids.First().Value.Qty;
            }
        }
        public override decimal BestAskQty
        {
            get
            {
                lock (locker) return asks.Count == 0 ? 0 : asks.First().Value.Qty;
            }
        }
        public override string BidsString
        {
            get
            {
                lock (locker) return MakeLevelsStr("bids", bids, false);
            }
        }
        public override string AsksString 
        {
            get
            {
                lock (locker) return MakeLevelsStr("asks", asks, true);
            }
        }

        string MakeLevelsStr(string side, SortedDictionary<decimal, Order<TOrderId>> levels, bool shouldReverse)
        {
            IEnumerable<Order<TOrderId>> chosenLevels = levels.Values.Take(numLevelsToSend);
            if (shouldReverse) chosenLevels = chosenLevels.Reverse();

            IEnumerable<string> chosenLevelsStrings =
                chosenLevels.Select(level =>
                                        $"P={level.Price.ToString(CultureInfo.InvariantCulture)} Q={level.Qty.ToString(CultureInfo.InvariantCulture)}");
            return $"{side}:\n{string.Join('\n', chosenLevelsStrings)}";
        }        

        public UnlimitedOrderBook(int numLevelsToSend, bool useMatching, int errorQueueWindowMs)
        {
            Contract.Requires(numLevelsToSend > 0, "numLevels has to be > 0");

            this.numLevelsToSend = numLevelsToSend;
            this.useMatching = useMatching;
            ErrorsTimeQ = new TimeCircularQueue<BookErrors>(errorQueueWindowMs);
        }

        public void Insert(OrderSide side, decimal price, decimal qty, TOrderId id)
        {
            Contract.Requires(price >= 0, "price has to be >= 0");
            Contract.Requires(qty >= 0, "qty has to be >= 0");

            var order = new Order<TOrderId>();
            order.SetAll(price, qty, id);

            SortedDictionary<decimal, Order<TOrderId>> book = null;
            ConcurrentDictionary<TOrderId, Order<TOrderId>> ordersById = null;
            if (side == OrderSide.Buy)
            {
                book = bids;
                ordersById = bidsById;
            }
            else if (side == OrderSide.Sell)
            {
                book = asks;
                ordersById = asksById;
            }

            Contract.Assume(book != null);

            try
            {
                lock (locker) book.Add(price, order);
            }
            catch (ArgumentException)
            {
                throw new ArgumentException($"Book side {side} already contains price level {price}: {book[price]}");
            }

            try
            {
                ordersById.TryAdd(id, order);
            }
            catch (ArgumentException)
            {
                throw new ArgumentException($"OrdersById with side {side} already contains id {id}: {ordersById[id]}");
            }
        }

        public void Update(OrderSide side, decimal qty, TOrderId id, ILogger logger = null)
        {
            Contract.Requires(qty >= 0, "qty has to be >= 0");

            ConcurrentDictionary<TOrderId, Order<TOrderId>> ordersById = null;
            if (side == OrderSide.Buy) ordersById = bidsById;
            else if (side == OrderSide.Sell) ordersById = asksById;

            Contract.Assume(ordersById != null);

            if (ordersById.TryGetValue(id, out Order<TOrderId> order)) order.SetQty(qty);
            else
            {
                ErrorsTimeQ.Enqueue(BookErrors.UpdateNoId, DateTime.UtcNow);
                logger?.Enqueue($"Book Error. Id {id} was not found in ordersById for side {side} while updating.");
            }

            //ErrorsTimeQ.Enqueue(BookErrors.UpdateNoId, DateTime.UtcNow);
        }

        public void InsertOrUpdate(OrderSide side, decimal price, decimal qty, TOrderId id)
        {
            Contract.Requires(price >= 0, "price has to be >= 0");
            Contract.Requires(qty >= 0, "qty has to be >= 0");

            ConcurrentDictionary<TOrderId, Order<TOrderId>> ordersById = null;
            if (side == OrderSide.Buy) ordersById = bidsById;
            else if (side == OrderSide.Sell) ordersById = asksById;

            Contract.Assume(ordersById != null);

            if (ordersById.TryGetValue(id, out Order<TOrderId> order)) order.SetQty(qty);
            else if (price > 0) Insert(side, price, qty, id);
        }

        public void InsertDeleteOrUpdateQty(OrderSide side, decimal price, decimal qty, TOrderId id)
        {
            Contract.Requires(price >= 0, "price has to be >= 0");
            Contract.Requires(qty >= 0, "qty has to be >= 0");

            SortedDictionary<decimal, Order<TOrderId>> book = null;
            ConcurrentDictionary<TOrderId, Order<TOrderId>> ordersById = null;
            if (side == OrderSide.Buy)
            {
                book = bids;
                ordersById = bidsById;
            }
            else if (side == OrderSide.Sell)
            {
                book = asks;
                ordersById = asksById;
            }

            Contract.Assume(ordersById != null);

            //отрицательные qty означают, что нужно уменьшить количество в priceLevel
            if (ordersById.TryGetValue(id, out Order<TOrderId> order))
            {
                order.SetQty(order.Qty + qty);
                if (order.Qty <= 0)
                {
                    ordersById.Remove(id, out order);
                    lock(locker) book.Remove(order.Price);
                }
            }
            else if (qty > 0) Insert(side, price, qty, id);
        }

        public void Delete(OrderSide side, TOrderId id, ILogger logger = null)
        {
            SortedDictionary<decimal, Order<TOrderId>> book = null;
            ConcurrentDictionary<TOrderId, Order<TOrderId>> ordersById = null;
            if (side == OrderSide.Buy)
            {
                book = bids;
                ordersById = bidsById;
            }
            else if (side == OrderSide.Sell)
            {
                book = asks;
                ordersById = asksById;
            }

            Contract.Assume(book != null);

            if (!ordersById.Remove(id, out Order<TOrderId> order))
            {
                ErrorsTimeQ.Enqueue(BookErrors.DeleteNoId, DateTime.UtcNow);
                logger?.Enqueue($"Book Error. Id {id} was not found in ordersById for side {side} while deleting.");
                return;
            }

            bool removeSucceeded;
            lock (locker) removeSucceeded = book.Remove(order.Price);
            if (!removeSucceeded)
            {
                if (!useMatching) throw new KeyNotFoundException($"Book Error. Price level {order.Price} was not found in book for side {side}.");
                if (!removedWhileMatching.Remove(order.Price))
                    throw new KeyNotFoundException($"Book Error. Price level {order.Price} was not found in removedWhileMatching list for side {side}.");
            }

            //ErrorsTimeQ.Enqueue(BookErrors.DeleteNoId, DateTime.UtcNow);
        }

        public void MatchDownToPrice(OrderSide side, decimal price, ILogger logger = null)
        {
            Contract.Requires(price >= 0, "price has to be >= 0");

            SortedDictionary<decimal, Order<TOrderId>>      book       = null;
            ConcurrentDictionary<TOrderId, Order<TOrderId>> ordersById = null;
            if (side == OrderSide.Buy)
            {
                book       = bids;
                ordersById = bidsById;
            }
            else if (side == OrderSide.Sell)
            {
                book       = asks;
                ordersById = asksById;
            }

            Contract.Assume(book != null);
            if (book.Count == 0)
            {
                ErrorsTimeQ.Enqueue(BookErrors.MatchDownEmptyInitial, DateTime.UtcNow);
                logger?.Enqueue($"Book error. Book side {side} is empty while trying to MatchDownToPrice {price}. Initial.");
                return;
            }
            KeyValuePair<decimal, Order<TOrderId>> topPair  = book.First();
            decimal                                topPrice = topPair.Key;
            TOrderId                               topId    = topPair.Value.Id;

            while (book.Count > 0 && (side == OrderSide.Sell && price >= BestAsk || side == OrderSide.Buy && price <= BestBid))
            {
                lock (locker)
                {
                    book.Remove(topPrice);
                    ordersById.Remove(topId, out _);
                }

                if (book.Count == 0)
                {
                    ErrorsTimeQ.Enqueue(BookErrors.MatchDownEmptyLoop, DateTime.UtcNow);
                    logger?.Enqueue($"Book error. Book side {side} is empty while trying to MatchDownToPrice {price}. Loop.");
                    return;
                }

                topPair  = book.First();
                topPrice = topPair.Key;
                topId    = topPair.Value.Id;

                removedWhileMatching.Add(topPrice);
                //logger?.Enqueue($"Removed price level {topPrice} ahead trade {price} on side {side} with id {topPrice.GetHashCode()}");
            }
        }

        public void DeleteLevelsAheadPrice(OrderSide side, decimal price, ILogger logger = null)
        {
            Contract.Requires(price >= 0, "price has to be >= 0");

            SortedDictionary<decimal, Order<TOrderId>>      book       = null;
            ConcurrentDictionary<TOrderId, Order<TOrderId>> ordersById = null;
            if (side == OrderSide.Buy)
            {
                book       = bids;
                ordersById = bidsById;
            }
            else if (side == OrderSide.Sell)
            {
                book       = asks;
                ordersById = asksById;
            }

            Contract.Assume(book != null);

            if (book.Count == 0)
            {
                ErrorsTimeQ.Enqueue(BookErrors.DeleteAheadEmptyInitial, DateTime.UtcNow);
                logger?.Enqueue($"Book error. Book side {side} is empty while trying to DeleteLevelsAheadPrice {price}. Initial.");
                return;
            }
            KeyValuePair<decimal, Order<TOrderId>> topPair = book.First();
            decimal topPrice = topPair.Key;
            TOrderId topId = topPair.Value.Id;

            lock (locker)
            {
                while (book.Count > 0 && (side == OrderSide.Sell && price > topPrice || side == OrderSide.Buy && price < topPrice))
                {
                    book.Remove(topPrice);
                    ordersById.Remove(topId, out _);

                    if (book.Count == 0)
                    {
                        ErrorsTimeQ.Enqueue(BookErrors.DeleteAheadEmptyLoop, DateTime.UtcNow);
                        logger?.Enqueue($"Book error. Removed all Book side {side} while trying to DeleteLevelsAheadPrice {price}. Loop.");
                        return;
                    }
                    topPair  = book.First();
                    topPrice = topPair.Key;
                    topId    = topPair.Value.Id;
                }
            }
        }

        public void DeleteByPrice(OrderSide side, decimal price, decimal qty, ILogger logger = null)
        {
            Contract.Requires(price >= 0, "price has to be >= 0");
            Contract.Requires(qty >= 0, "qty has to be >= 0");

            SortedDictionary<decimal, Order<TOrderId>> book = null;
            if (side == OrderSide.Buy) book = bids;
            else if (side == OrderSide.Sell) book = asks;

            Contract.Assume(book != null);

            lock (locker)
            {
                if (book.TryGetValue(price, out Order<TOrderId> order))
                {
                    decimal remainingQty = order.Qty - qty;

                    if (remainingQty <= 0)
                    {
                        book.Remove(price);
                        removedWhileMatching.Add(price);
                        ErrorsTimeQ.Enqueue(BookErrors.MatchedPriceLevel, DateTime.UtcNow);
                        logger?.Enqueue($"Executed price level {price} on side {side}  with id {price.GetHashCode()} and removed");

                        //if (remainingQty == 0) log;
                    }
                    else if (remainingQty > 0) order.SetQty(remainingQty);
                }
            }
        }

        public void Clear()
        {
            BaseClear();
            lock (locker)
            {
                bids.Clear();
                asks.Clear();

                bidsById.Clear();
                asksById.Clear();
            }

            removedWhileMatching.Clear();
        }

        public void ClearOneSide(OrderSide side)
        {
            lock (locker)
            {
                if (side == OrderSide.Buy)
                {
                    bids.Clear();
                    bidsById.Clear();
                }
                else
                {
                    asks.Clear();
                    asksById.Clear();
                }
            }
        }

        public override decimal GetOneSideVwap(OrderSide side, decimal vwapQty)
        {
            Contract.Requires(vwapQty > 0, "vwapQty has to be > 0");

            SortedDictionary<decimal, Order<TOrderId>> book = null;

            if (side == OrderSide.Buy) book = bids;
            else if (side == OrderSide.Sell) book = asks;

            Contract.Assume(book != null);
            decimal vwap;
            lock (locker) vwap = GetOneSideVwapInternal(side, vwapQty, book.Values);

            return Math.Truncate(vwap * TruncCoef) / TruncCoef;
        }
    }
}
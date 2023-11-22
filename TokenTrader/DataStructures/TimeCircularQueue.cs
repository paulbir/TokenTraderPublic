using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace TokenTrader.DataStructures
{
    public class TimeCircularQueue<T> : IEnumerable<T>
    {
        readonly Queue<T>        dataWindow = new Queue<T>();
        readonly Queue<DateTime> timestamps = new Queue<DateTime>();
        readonly int             windowMs;

        bool alreadyRemovedSome;

        public bool IsFull   => alreadyRemovedSome;
        public int  WindowMs => windowMs;
        /// <summary>
        /// Gets the number of elements contained in this queue.
        /// </summary>
        /// <returns>The number of elements contained in this queue.</returns>
        public int Count => dataWindow.Count;

        public TimeCircularQueue(int windowMs)
        {
            this.windowMs = windowMs;
        }

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IEnumerator`1"/> that can be used to iterate through the collection.
        /// </returns>
        public IEnumerator<T> GetEnumerator()
        {
            return dataWindow.GetEnumerator();
        }

        /// <summary>
        /// Returns an enumerator that iterates through a collection.
        /// </summary>
        /// <returns>
        /// An <see cref="T:System.Collections.IEnumerator"/> object that can be used to iterate through the collection.
        /// </returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Inserts a single element at the back of this queue.
        /// </summary>
        /// <param name="value">The element to insert.</param>
        /// <param name="timestamp">Last timestamp to determine which old values to delete.</param>
        public void Enqueue(T value, DateTime timestamp)
        {
            dataWindow.Enqueue(value);
            timestamps.Enqueue(timestamp);

            DateTime farthestTimestamp = timestamps.Peek();
            while ((timestamp - farthestTimestamp).TotalMilliseconds > windowMs)
            {
                timestamps.Dequeue();
                dataWindow.Dequeue();

                farthestTimestamp = timestamps.Peek();
                alreadyRemovedSome = true;
            }
        }

        /// <summary>
        /// Inserts a single non default element at the back of this queue.
        /// </summary>
        /// <param name="value">The element to insert.</param>
        /// <param name="timestamp">Last timestamp to determine which old values to delete.</param>
        public void EnqueueNonDefault(T value, DateTime timestamp)
        {
            if(EqualityComparer<T>.Default.Equals(value, default(T))) return;

            Enqueue(value, timestamp);
        }

        /// <summary>
        /// Returns the first element of this queue.
        /// </summary>
        /// <returns>First element.</returns>
        public T First()
        {
            return dataWindow.First();
        }

        /// <summary>
        /// Returns the last element of this queue.
        /// </summary>
        /// <returns>Last element.</returns>
        public T Last()
        {
            return dataWindow.Last();
        }

        /// <summary>
        /// Returns the element with minimum value of this queue.
        /// </summary>
        /// <returns>Element with minimum value.</returns>
        public T Min()
        {
            return dataWindow.Min();
        }

        /// <summary>
        /// Returns the element with maximum value of this queue.
        /// </summary>
        /// <returns>Element with maximum value.</returns>
        public T Max()
        {
            return dataWindow.Max();
        }

        /// <summary>
        /// Removes all items from this queue.
        /// </summary>
        public void Clear()
        {
            dataWindow.Clear();
            timestamps.Clear();
            alreadyRemovedSome = false;
        }
    }
}
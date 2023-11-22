using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Nito.Collections;

namespace TokenTrader.DataStructures
{
    public class CircularQueue<T> : IEnumerable<T> where T : IComparable
    {
        readonly Deque<T> dataWindow;
        readonly bool isFastMaxMinEnabled;
        readonly Deque<int> maxIndexes;
        readonly Deque<int> minIndexes;
        readonly int windowSize;
        int index;
        int tmpExtremumIndex;

        /// <summary>
        /// Gets the number of elements contained in this queue.
        /// </summary>
        /// <returns>The number of elements contained in this queue.</returns>
        public int Count => dataWindow.Count;

        public bool IsFull => Count == windowSize;

        public CircularQueue(int windowSize, bool isFastMaxMinEnabled = false)
        {
            this.windowSize = windowSize;
            this.isFastMaxMinEnabled = isFastMaxMinEnabled;

            if (isFastMaxMinEnabled)
            {
                maxIndexes = new Deque<int>();
                minIndexes = new Deque<int>();
            }
            dataWindow = new Deque<T>(windowSize + 1);
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
        public void CircularEnqueue(T value)
        {
            if (isFastMaxMinEnabled && dataWindow.Count > 0)
            {
                if (value.CompareTo(dataWindow.Last()) > 0)
                {
                    minIndexes.AddToBack(index - 1);
                    if (index - windowSize == minIndexes.First()) minIndexes.RemoveFromFront();
                    while (maxIndexes.Count > 0)
                    {
                        tmpExtremumIndex = maxIndexes.Last();
                        if (index >= windowSize) tmpExtremumIndex -= index - windowSize;

                        if (value.CompareTo(dataWindow[tmpExtremumIndex]) <= 0)
                        {
                            if (index - windowSize == maxIndexes.First()) maxIndexes.RemoveFromFront();
                            break;
                        }
                        maxIndexes.RemoveFromBack();
                    }
                }
                else
                {
                    maxIndexes.AddToBack(index - 1);
                    if (index - windowSize == maxIndexes.First()) maxIndexes.RemoveFromFront();

                    while (minIndexes.Count > 0)
                    {
                        tmpExtremumIndex = minIndexes.Last();
                        if (index >= windowSize) tmpExtremumIndex -= index - windowSize;

                        if (value.CompareTo(dataWindow[tmpExtremumIndex]) >= 0)
                        {
                            if (index - windowSize == minIndexes.First()) minIndexes.RemoveFromFront();
                            break;
                        }
                        minIndexes.RemoveFromBack();
                    }
                }
            }

            index++;

            dataWindow.AddToBack(value);
            if (dataWindow.Count > windowSize) dataWindow.RemoveFromFront();
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
        /// Efficiently calculates and returns the element with minimum value of this queue.
        /// </summary>
        /// <returns>Element with minimum value.</returns>
        public T FastMin()
        {
            if (!isFastMaxMinEnabled) throw new NotSupportedException("Fast Min and Max handling is disabled");
            if (dataWindow.Count >= windowSize)
            {
                //если в массиве индексов не пусто, то минимум находится по индексу первого элемента из массива. иначе минимум - это последний элемент в окне
                return dataWindow[minIndexes.Count > 0 ? minIndexes.First() - (index - windowSize) : windowSize - 1];
            }
            throw new InvalidOperationException("FastMin cannot be called when Queue.Count < window size");
        }

        /// <summary>
        /// Efficiently calculates and returns the element with maximum value of this queue.
        /// </summary>
        /// <returns>Element with maximum value.</returns>
        public T FastMax()
        {
            if (!isFastMaxMinEnabled) throw new NotSupportedException("Fast Min and Max handling is disabled");
            if (dataWindow.Count >= windowSize)
            {
                //если в массиве индексов не пусто, то максимум находится по индексу первого элемента из массива. иначе максимум - это последний элемент в окне
                return dataWindow[maxIndexes.Count > 0 ? maxIndexes.First() - (index - windowSize) : windowSize - 1];
            }
            throw new InvalidOperationException("FastMax cannot be called when Queue.Count < window size");
        }

        /// <summary>
        /// Removes all items from this queue.
        /// </summary>
        public void Clear()
        {
            maxIndexes?.Clear();
            minIndexes?.Clear();
            dataWindow.Clear();
            index = 0;
            tmpExtremumIndex = 0;
        }
    }
}
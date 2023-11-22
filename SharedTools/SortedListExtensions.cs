using System;
using System.Collections.Generic;

namespace SharedTools
{
    public static class SortedListExtensions
    {
        public static int IndexOfApproximateSearchedKey<TValue>(this SortedList<decimal, TValue> list, decimal key)
        {
            // Check to see if we need to search the list.
            if (list == null || list.Count <= 0) return -1;
            if (list.Count == 1) return 0;

            // Setup the variables needed to find the closest index
            int lower = 0;
            int upper = list.Count - 1;
            int index = (lower + upper) / 2;

            // Find the closest index (rounded down)
            while (lower <= upper)
            {
                int comparisonResult = key.CompareTo(list.Keys[index]);
                if (comparisonResult == 0) return index;

                if (comparisonResult < 0) upper = index - 1;
                else lower = index + 1;

                index = (lower + upper) / 2;
            }

            // Check to see if we are under or over the max values.
            if (index >= list.Count - 1) return list.Count - 1;
            if (index < 0) return 0;

            // Check to see if we should have rounded up instead
            if (list.Keys[index + 1] - key < key - list.Keys[index]) { index++; }

            // Return the correct/closest string
            return index;
        }

        public static int IndexOfNearestGreaterKeyBinary<TKey, TValue>(this SortedList<TKey, TValue> list, TKey key) where TKey : IComparable
        {
            // Check to see if we need to search the list.
            if (list == null || list.Count <= 0) return -1;
            if (list.Count == 1) return key.CompareTo(list.Keys[0]) < 0 ? 0 : -1;

            // Setup the variables needed to find the closest index
            int lower = 0;
            int upper = list.Count - 1;
            int index = (lower + upper) / 2;

            // Find the closest index (rounded down)
            while (lower <= upper)
            {
                int comparisonResult = key.CompareTo(list.Keys[index]);
                //int comparisonResult = key.CompareTo(list.Keys[index]);
                if (comparisonResult == 0) return index + 1;

                if (comparisonResult < 0) upper = index - 1;
                else lower = index + 1;

                index = (lower + upper) / 2;
            }

            // Check to see if we are under or over the max values.
            if (index >= list.Count - 1) return list.Count - 1;
            if (index < 0) return 0;

            //// Check to see if we should have rounded up instead
            //if (list.Keys[index + 1] - key < key - list.Keys[index]) { index++; }

            // Return the correct/closest string
            return index + 1;
        }

        public static int IndexOfNearestGreaterEqualKey<TKey, TValue>(this SortedList<TKey, TValue> list, TKey key) where TKey : IComparable
        {
            if (list == null || list.Count <= 0) return -1;
            if (list.Count == 1) return key.CompareTo(list.Keys[0]) <= 0 ? 0 : -1;
            if (list.Keys[0].CompareTo(list.Keys[1]) > 0) //реверсивный порядок
            {
                if (key.CompareTo(list.Keys[0]) > 0) return -1; //в начале наименьшее значение < key. значит все меньше
            }
            else if (key.CompareTo(list.Keys[list.Count - 1]) > 0) return -1;

            for (int i = 0; i < list.Count; i++) if (key.CompareTo(list.Keys[i]) <= 0) return i;

            throw new IndexOutOfRangeException($"Couldn't fine Greater for key={key} in keys:{string.Join(';', list.Keys)}.");
        }

        public static int IndexOfNearestLessEqualKey<TKey, TValue>(this SortedList<TKey, TValue> list, TKey key) where TKey : IComparable
        {
            if (list == null || list.Count <= 0) return -1;
            if (list.Count == 1) return key.CompareTo(list.Keys[0]) >= 0 ? 0 : -1;
            if (list.Keys[0].CompareTo(list.Keys[1]) > 0) //реверсивный порядок
            {
                if (key.CompareTo(list.Keys[list.Count - 1]) < 0) return -1; //в начале наименьшее значение < key. значит все меньше
            }
            else if (key.CompareTo(list.Keys[0]) < 0) return -1;

            for (int i = 0; i < list.Count; i++) if (key.CompareTo(list.Keys[i]) >= 0) return i;

            throw new IndexOutOfRangeException($"Couldn't find Less for key={key} in keys:{string.Join(';', list.Keys)}.");
        }

        public static int IndexOfFirstKeyWithMaxKeysDifference<TValue>(this SortedList<decimal, TValue> list)
        {
            if (list == null || list.Count <= 1) return -1;

            decimal maxKeysDiff = 0;
            int idxOfFirstMaxDiff = -1;
            for (int i = 0; i < list.Count - 1; i++)
            {
                decimal firstKey = list.Keys[i];
                decimal nextKey = list.Keys[i + 1];
                decimal diff = Math.Abs(nextKey - firstKey);

                if (diff <= maxKeysDiff) continue;

                maxKeysDiff = diff;
                idxOfFirstMaxDiff = i;
            }

            return idxOfFirstMaxDiff;
        }

        //public static int IndexOfNearestLessKey<TKey, TValue>(this SortedList<TKey, TValue> list, TKey key) where TKey : IComparable
        //{
        //    // Check to see if we need to search the list.
        //    if (list == null || list.Count <= 0) return -1;
        //    if (list.Count == 1) return list.Comparer.Compare(key, list.Keys[0]) < 0 ? 0 : -1;

        //    // Setup the variables needed to find the closest index
        //    int lower = 0;
        //    int upper = list.Count - 1;
        //    int index = (lower + upper) / 2;

        //    // Find the closest index (rounded up)
        //    while (lower <= upper)
        //    {
        //        //int comparisonResult = key.CompareTo(list.Keys[index]);
        //        int comparisonResult = list.Comparer.Compare(key, list.Keys[index]);
        //        if (comparisonResult == 0) return index + 1;

        //        if (comparisonResult > 0) upper = index - 1;
        //        else lower = index + 1;

        //        index = (lower + upper) / 2;
        //    }

        //    // Check to see if we are under or over the max values.
        //    if (index >= list.Count - 1) return list.Count - 1;
        //    if (index < 0) return 0;

        //    //// Check to see if we should have rounded up instead
        //    //if (list.Keys[index + 1] - key < key - list.Keys[index]) { index++; }

        //    // Return the correct/closest string
        //    return index + 1;
        //}
    }
}

// *************************************************
// Created by Aron Weiler
// Feel free to use this code in any way you like, 
// just don't blame me when your coworkers think you're awesome.
// Comments?  Email aronweiler@gmail.com
// Revision 1.6
// Revised locking strategy based on the some bugs found with the existing lock objects. 
// Possible deadlock and race conditions resolved by swapping out the two lock objects for a ReaderWriterLockSlim. 
// Performance takes a very small hit, but correctness is guaranteed.
// *************************************************

using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TokenTrader.DataStructures
{
    /// <summary>
    /// Multi-Key Dictionary Class
    /// </summary>    
    /// <typeparam name="TPrimaryKey">Primary Key Type</typeparam>
    /// <typeparam name="TSubKey">Sub Key Type</typeparam>
    /// <typeparam name="TValue">Value Type</typeparam>
    class MultiKeyDictionary<TPrimaryKey, TSubKey, TValue>
    {
        internal readonly Dictionary<TPrimaryKey, TValue>  baseDictionary         = new Dictionary<TPrimaryKey, TValue>();
        internal readonly Dictionary<TSubKey, TPrimaryKey> subDictionary          = new Dictionary<TSubKey, TPrimaryKey>();
        internal readonly Dictionary<TPrimaryKey, TSubKey> primaryToSubkeyMapping = new Dictionary<TPrimaryKey, TSubKey>();

        readonly ReaderWriterLockSlim readerWriterLock = new ReaderWriterLockSlim();

        public TValue this[TSubKey subKey]
        {
            get
            {
                if (TryGetValue(subKey, out TValue item)) return item;
                throw new KeyNotFoundException("sub key not found: " + subKey);
            }
        }

        public TValue this[TPrimaryKey primaryKey]
        {
            get
            {
                if (TryGetValue(primaryKey, out TValue item)) return item;
                throw new KeyNotFoundException("primary key not found: " + primaryKey);
            }
        }

        public List<TValue> Values
        {
            get
            {
                readerWriterLock.EnterReadLock();

                try { return baseDictionary.Values.ToList(); }
                finally { readerWriterLock.ExitReadLock(); }
            }
        }

        public int Count
        {
            get
            {
                readerWriterLock.EnterReadLock();

                try { return baseDictionary.Count; }
                finally { readerWriterLock.ExitReadLock(); }
            }
        }

        public void Associate(TSubKey subKey, TPrimaryKey primaryKey)
        {
            readerWriterLock.EnterUpgradeableReadLock();

            try
            {
                if (!baseDictionary.ContainsKey(primaryKey))
                    throw new KeyNotFoundException($"The base dictionary does not contain the key '{primaryKey}'");

                if (primaryToSubkeyMapping.ContainsKey(primaryKey)) // Remove the old mapping first
                {
                    readerWriterLock.EnterWriteLock();

                    try
                    {
                        subDictionary.Remove(primaryToSubkeyMapping[primaryKey]);
                        primaryToSubkeyMapping.Remove(primaryKey);
                    }
                    finally { readerWriterLock.ExitWriteLock(); }
                }

                subDictionary[subKey]              = primaryKey;
                primaryToSubkeyMapping[primaryKey] = subKey;
            }
            finally { readerWriterLock.ExitUpgradeableReadLock(); }
        }

        public bool TryGetValue(TSubKey subKey, out TValue val)
        {
            val = default;

            readerWriterLock.EnterReadLock();

            try
            {
                if (subDictionary.TryGetValue(subKey, out TPrimaryKey primaryKey)) { return baseDictionary.TryGetValue(primaryKey, out val); }
            }
            finally { readerWriterLock.ExitReadLock(); }

            return false;
        }

        public bool TryGetValue(TPrimaryKey primaryKey, out TValue val)
        {
            readerWriterLock.EnterReadLock();

            try { return baseDictionary.TryGetValue(primaryKey, out val); }
            finally { readerWriterLock.ExitReadLock(); }
        }

        public bool ContainsKey(TSubKey subKey)
        {
            return TryGetValue(subKey, out TValue _);
        }

        public bool ContainsKey(TPrimaryKey primaryKey)
        {
            return TryGetValue(primaryKey, out TValue _);
        }

        public void Remove(TPrimaryKey primaryKey)
        {
            readerWriterLock.EnterWriteLock();

            try
            {
                if (primaryToSubkeyMapping.ContainsKey(primaryKey))
                {
                    subDictionary.Remove(primaryToSubkeyMapping[primaryKey]);
                    primaryToSubkeyMapping.Remove(primaryKey);
                }

                baseDictionary.Remove(primaryKey);
            }
            finally { readerWriterLock.ExitWriteLock(); }
        }

        public void Remove(TSubKey subKey)
        {
            readerWriterLock.EnterWriteLock();

            try
            {
                baseDictionary.Remove(subDictionary[subKey]);

                primaryToSubkeyMapping.Remove(subDictionary[subKey]);

                subDictionary.Remove(subKey);
            }
            finally { readerWriterLock.ExitWriteLock(); }
        }

        public void Add(TPrimaryKey primaryKey, TValue val)
        {
            readerWriterLock.EnterWriteLock();

            try { baseDictionary.Add(primaryKey, val); }
            finally { readerWriterLock.ExitWriteLock(); }
        }

        public void Add(TPrimaryKey primaryKey, TSubKey subKey, TValue val)
        {
            Add(primaryKey, val);

            Associate(subKey, primaryKey);
        }

        public TValue[] CloneValues()
        {
            readerWriterLock.EnterReadLock();

            try
            {
                TValue[] values = new TValue[baseDictionary.Values.Count];

                baseDictionary.Values.CopyTo(values, 0);

                return values;
            }
            finally { readerWriterLock.ExitReadLock(); }
        }

        public TPrimaryKey[] ClonePrimaryKeys()
        {
            readerWriterLock.EnterReadLock();

            try
            {
                TPrimaryKey[] values = new TPrimaryKey[baseDictionary.Keys.Count];

                baseDictionary.Keys.CopyTo(values, 0);

                return values;
            }
            finally { readerWriterLock.ExitReadLock(); }
        }

        public TSubKey[] CloneSubKeys()
        {
            readerWriterLock.EnterReadLock();

            try
            {
                TSubKey[] values = new TSubKey[subDictionary.Keys.Count];

                subDictionary.Keys.CopyTo(values, 0);

                return values;
            }
            finally { readerWriterLock.ExitReadLock(); }
        }

        public void Clear()
        {
            readerWriterLock.EnterWriteLock();

            try
            {
                baseDictionary.Clear();

                subDictionary.Clear();

                primaryToSubkeyMapping.Clear();
            }
            finally { readerWriterLock.ExitWriteLock(); }
        }

        public IEnumerator<KeyValuePair<TPrimaryKey, TValue>> GetEnumerator()
        {
            readerWriterLock.EnterReadLock();

            try { return baseDictionary.GetEnumerator(); }
            finally { readerWriterLock.ExitReadLock(); }
        }
    }
}
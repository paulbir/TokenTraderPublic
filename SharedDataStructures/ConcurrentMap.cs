using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SharedDataStructures
{
    public class ConcurrentMap<T1, T2> : IEnumerable<KeyValuePair<T1, T2>>
    {
        public class Indexer<T3, T4>
        {
            readonly ConcurrentDictionary<T3, T4> dictionary;

            public T4 this[T3 index]
            {
                get => dictionary[index];
                set => dictionary[index] = value;
            }

            public Indexer(ConcurrentDictionary<T3, T4> dictionary)
            {
                this.dictionary = dictionary;
            }

            public bool Contains(T3 key)
            {
                return dictionary.ContainsKey(key);
            }

            public bool TryGetValue(T3 key, out T4 value)
            {
                return dictionary.TryGetValue(key, out value);
            }

            public bool TryRemove(T3 key, out T4 value)
            {
                return dictionary.TryRemove(key, out value);
            }
        }

        readonly ConcurrentDictionary<T1, T2> forward = new ConcurrentDictionary<T1, T2>();
        readonly ConcurrentDictionary<T2, T1> reverse = new ConcurrentDictionary<T2, T1>();

        public Indexer<T1, T2> Forward { get; }
        public Indexer<T2, T1> Reverse { get; }

        public ConcurrentMap()
        {
            Forward = new Indexer<T1, T2>(forward);
            Reverse = new Indexer<T2, T1>(reverse);
        }

        public ConcurrentMap(IDictionary<T1, T2> initialDictionary)
        {
            Forward = new Indexer<T1, T2>(forward);
            Reverse = new Indexer<T2, T1>(reverse);

            foreach (KeyValuePair<T1, T2> pair in initialDictionary) TryAdd(pair.Key, pair.Value);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerator<KeyValuePair<T1, T2>> GetEnumerator()
        {
            return forward.GetEnumerator();
        }

        public bool TryAdd(T1 t1, T2 t2)
        {
            return forward.TryAdd(t1, t2) && reverse.TryAdd(t2, t1);
        }

        public bool Remove(T1 t1, T2 t2)
        {
            return forward.TryRemove(t1, out _) && reverse.TryRemove(t2, out _);
        }

        public void Clear()
        {
            forward.Clear();
            reverse.Clear();
        }
    }
}
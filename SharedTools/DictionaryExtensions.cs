using System;
using System.Collections.Generic;

namespace SharedTools
{
    public static class DictionaryExtensions
    {
        public static bool TryGetTypedValue<TKey, TValue, TActual>(this IDictionary<TKey, TValue> data,
                                                                   TKey                           key,
                                                                   out TActual                    value,
                                                                   Func<TValue, TActual>          converter = null) where TActual : TValue
        {
            value = default;

            if (data.TryGetValue(key, out TValue storedValue))
            {
                if (converter != null)
                {
                    value = converter(storedValue);
                    return true;
                }

                if (storedValue is TActual actualTypeValue)
                {
                    value = actualTypeValue;
                    return true;
                }

                return false;
            }

            return false;
        }
    }
}
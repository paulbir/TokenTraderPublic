using System;
using System.Globalization;

namespace SharedTools
{
    public static class StringExtensions
    {
        public static int ToInt32(this string value)
        {
            return Convert.ToInt32(value);
        }

        public static long ToLong(this string value)
        {
            return Convert.ToInt64(value);
        }

        public static double ToDouble(this string value)
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        public static bool ToBool(this string value)
        {
            return Convert.ToBoolean(value);
        }

        public static decimal ToDecimal(this string value)
        {
            return decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        public static int GetStableHashCode(this string str)
        {
            unchecked
            {
                int hash1 = 5381;
                int hash2 = hash1;

                for (int i = 0; i < str.Length && str[i] != '\0'; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1 || str[i + 1] == '\0')
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }

                return hash1 + (hash2 * 1566083941);
            }
        }

        public static long GetPositiveStableHashCode(this string str)
        {
            long hash = GetStableHashCode(str);
            return int.MaxValue + hash;
        }

        public static string FirstLetterToUpper(this string str)
        {
            if (str == null)
                return null;

            if (str.Length > 1)
                return char.ToUpper(str[0]) + str.Substring(1);

            return str.ToUpper();
        }
    }
}
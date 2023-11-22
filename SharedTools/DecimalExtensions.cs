using System.Globalization;

namespace SharedTools
{
    public static class DecimalExtensions
    {
        public static decimal Normalize(this decimal value)
        {
            return value / 1.000000000000000000000000000000000m;
        }

        public static string ToStringNoZeros(this decimal value)
        {
            return value.ToString("0.#############################", CultureInfo.InvariantCulture);
        }
    }
}
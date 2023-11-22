using System.Globalization;

namespace SharedDataStructures.Messages
{
    public class LimitMessage
    {
        public          string  Name       { get; protected set; }
        public          decimal Min        { get; protected set; }
        public          decimal Max        { get; protected set; }
        public          decimal Exposure    { get; protected set; }
        public override string  ToString() => $"{Name}:" + 
                                              $"Min={Min.ToString("0.#############################", CultureInfo.InvariantCulture)}|" +
                                              $"Exposure={Exposure.ToString("0.#############################", CultureInfo.InvariantCulture)}|" +
                                              $"Max={Max.ToString("0.#############################", CultureInfo.InvariantCulture)}";
    }
}
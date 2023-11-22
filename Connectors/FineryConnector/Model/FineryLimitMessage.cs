using SharedDataStructures.Messages;

namespace FineryConnector.Model
{
    class FineryLimitMessage : LimitMessage
    {
        public decimal Free => Max - Exposure;
        public FineryLimitMessage(string name, decimal min, decimal exposure, decimal max)
        {
            Name    = name;
            Min     = min;
            Exposure = exposure;
            Max     = max;
        }

        public void Update(decimal exposure, decimal max)
        {
            Exposure = exposure;
            Max = max;
        }
    }
}
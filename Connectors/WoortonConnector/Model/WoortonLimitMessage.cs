using SharedDataStructures.Messages;

namespace WoortonConnector.Model
{
    class WoortonLimitMessage : LimitMessage
    {
        public WoortonLimitMessage(string name, decimal min, decimal max, decimal exposure)
        {
            Name    = name;
            Min     = min;
            Max     = max;
            Exposure = exposure;
        }
    }
}
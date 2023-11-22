using SharedDataStructures.Messages;

namespace WoortonV2Connector.Model
{
    class WoortonV2LimitMessage : LimitMessage
    {
        public WoortonV2LimitMessage(string name, decimal min, decimal max, decimal exposure)
        {
            Name     = name;
            Min      = min;
            Max      = max;
            Exposure = exposure;
        }
    }
}
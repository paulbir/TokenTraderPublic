using SharedDataStructures.Messages;

namespace OceanConnector.Model
{
    class OceanLimitMessage : LimitMessage
    {
        public OceanLimitMessage(string name, decimal min, decimal exposure, decimal max)
        {
            Name     = name;
            Min      = min;
            Exposure = exposure;
            Max      = max;
        }
    }
}
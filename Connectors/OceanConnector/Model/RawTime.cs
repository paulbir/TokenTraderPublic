using System;

namespace OceanConnector.Model
{
    class RawTime
    {
        public DateTime ServerTime { get; }

        public RawTime(long serverTime)
        {
            ServerTime = DateTimeOffset.FromUnixTimeSeconds(serverTime).UtcDateTime;
        }
    }
}
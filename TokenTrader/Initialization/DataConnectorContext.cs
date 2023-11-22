using System.Collections.Generic;
using SharedDataStructures.Interfaces;

namespace TokenTrader.Initialization
{
    class DataConnectorContext
    {
        public IDataConnector Connector { get; }
        public HashSet<string> IsinsToGet { get; } = new HashSet<string>();

        public DataConnectorContext(IDataConnector connector)
        {
            Connector = connector;
        }
    }
}
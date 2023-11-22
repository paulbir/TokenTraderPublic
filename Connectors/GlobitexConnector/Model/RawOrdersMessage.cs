using System.Collections.Generic;
using System.Linq;
using SharedDataStructures.Messages;

namespace GlobitexConnector.Model
{
    class RawOrdersMessage
    {
        public List<OrderMessage> ActiveOrders { get; }

        public RawOrdersMessage(List<GlobitexOrderStatusMessage> orders)
        {
            ActiveOrders = orders != null ? orders.Select(order => (OrderMessage)order).ToList() : new List<OrderMessage>();
        }
    }
}
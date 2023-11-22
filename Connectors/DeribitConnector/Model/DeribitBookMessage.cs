using System;
using System.Collections.Generic;
using SharedDataStructures.Messages;

namespace DeribitConnector.Model
{
    public class DeribitBookMessage : BookMessage
    {
        public DeribitBookMessage(string instrument_name, List<List<object>> bids, List<List<object>> asks, long change_id)
        {
            Isin = instrument_name;

            Bids = CreateBookPart(bids);
            Asks = CreateBookPart(asks);

            Sequence = change_id;

            List<PriceLevel> CreateBookPart(List<List<object>> levels)
            {
                var bookPart = new List<PriceLevel>();
                foreach (List<object> tokens in levels)
                {
                    string action = (string)tokens[0];
                    decimal price = Convert.ToDecimal(tokens[1]);
                    decimal qty = Convert.ToDecimal(tokens[2]);

                    if (action == "delete") qty = 0;

                    bookPart.Add(new DeribitPriceLevel(price, qty));
                }

                return bookPart;
            }
        }
    }
}
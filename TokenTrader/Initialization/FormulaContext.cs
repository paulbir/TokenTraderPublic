using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Flee.PublicTypes;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Messages;
using TokenTrader.State;

namespace TokenTrader.Initialization
{
    class FormulaContext
    {
        readonly IGenericExpression<decimal> expression;
        readonly Dictionary<string, PricesState> variablesPricesStates = new Dictionary<string, PricesState>();

        public string TradeIsin { get; }
        public decimal MinValue { get; private set; }
        public decimal MaxValue { get; private set; }

        public decimal CoefToBase { get; set; } = 0;
        public bool ValuesSet { get; set; }

        public string ExpressionString => expression.Text;
        public string VariablesValuesString => string.Join(';', expression.Context.Variables.Keys.Select(name => $"{name}={expression.Context.Variables[name]}"));
        public bool IsNumFormula => expression.Context.Variables.Keys.Count == 0;


        public FormulaContext(string tradeIsin, IGenericExpression<decimal> expression)
        {
            TradeIsin = tradeIsin;
            this.expression = expression;
        }

        //на случай, если формула состоит из чисел
        public void SetInitialValuesForNumFormula()
        {
            ValuesSet = true;

            decimal value = expression.Evaluate();
            MinValue = value;
            MaxValue = value;
        }

        public void TryStoreVariablesPricesStates(IDictionary<string, VariableData> variables)
        {
            if (variablesPricesStates.Count > 0) return;

            foreach (string variableName in expression.Context.Variables.Keys)
            {
                if (!variables.TryGetValue(variableName, out VariableData variableData))
                    throw new ConfigErrorsException("Formulas by variables names dictionary doesn't contain variable name " + 
                                                    $"{variableName} from formula: {expression.Text}.");

                variablesPricesStates.Add(variableName, variableData.PricesState);
            }
        }

        public void SetPrices(bool checkFormulaMatchPrices, out bool doPricesMatchFormula)
        {
            decimal bidsValue = CalcOneSideValue(OrderSide.Buy,  out decimal bid);
            decimal asksValue = CalcOneSideValue(OrderSide.Sell, out decimal ask);

            if (checkFormulaMatchPrices)
            {
                if (bidsValue != bid || asksValue != ask)
                {
                    doPricesMatchFormula = false;
                    return;
                }
            }

            MinValue = Math.Min(bidsValue, asksValue);
            MaxValue = Math.Max(bidsValue, asksValue);

            ValuesSet = true;
            doPricesMatchFormula = true;
            return;

            decimal CalcOneSideValue(OrderSide side, out decimal usedPrice)
            {
                usedPrice         = 0;
                foreach (KeyValuePair<string, PricesState> pair in variablesPricesStates)
                {
                    string variableName = pair.Key;
                    PricesState state = pair.Value;

                    usedPrice = side == OrderSide.Buy ? state.Book.BestBid : state.Book.BestAsk;
                    expression.Context.Variables[variableName] = usedPrice;
                }

                return expression.Evaluate();
            }
        }
    }
}
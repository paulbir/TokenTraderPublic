using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SharedTools;
using SharedTools.Interfaces;

namespace TokenTrader.State
{
    class AccountState
    {
        readonly ILogger logger;
        readonly CancellationTokenSource source;

        readonly AsyncManualResetEvent gotActiveOrdersResetEvent = new AsyncManualResetEvent();
        readonly AsyncManualResetEvent gotBalanceResetEvent = new AsyncManualResetEvent();
        readonly ConcurrentDictionary<string, decimal> marginPositions = new ConcurrentDictionary<string, decimal>();
        readonly Dictionary<string, decimal> limitsToStopOnHedgeExposureExceeding = new Dictionary<string, decimal>();

        public bool IsConnected { get; set; } = false;
        public ITradeConnector Connector { get; }
        public ConcurrentDictionary<string, decimal> AvailableSpotBalances { get; } = new ConcurrentDictionary<string, decimal>();

        public AccountState(ITradeConnector connector, ILogger logger, CancellationTokenSource source)
        {
            Connector = connector;
            this.logger = logger;
            this.source = source;
        }

        public void SetHedge(Dictionary<string, decimal> limitsToStopOnHedgeExposureExceedingParam)
        {
            foreach ((string currency, decimal maxExposure) in limitsToStopOnHedgeExposureExceedingParam)
            {
                //запоминаем минимальный maxExposure для выключения в случае приближения к лимиту
                if (limitsToStopOnHedgeExposureExceeding.TryGetValue(currency, out decimal currentMaxExposure))
                    limitsToStopOnHedgeExposureExceeding[currency] = Math.Min(maxExposure, currentMaxExposure);
                else limitsToStopOnHedgeExposureExceeding.Add(currency, maxExposure);
            }
        }

        public async Task WaitForActiveOrdersAsync(int timeoutMs, string connectorName, bool beforeExit)
        {
            try
            {
                CancellationToken timeoutToken = beforeExit ? new CancellationTokenSource(timeoutMs).Token : source.NewPairedTimeoutToken(timeoutMs);

                await gotActiveOrdersResetEvent.WaitAsync(timeoutToken);
                gotActiveOrdersResetEvent.Reset();
            }
            catch (TaskCanceledException)
            {
                logger.Enqueue($"Didn't receive active orders for connector {connectorName} in assigned timeout.");
            }
        }

        public void WaitForActiveOrders(int timeoutMs, string connectorName, bool beforeExit)
        {
            try
            {
                CancellationToken timeoutToken = beforeExit ? new CancellationTokenSource(timeoutMs).Token : source.NewPairedTimeoutToken(timeoutMs);

                gotActiveOrdersResetEvent.Wait(timeoutToken);
                gotActiveOrdersResetEvent.Reset();
            }
            catch (TaskCanceledException)
            {
                logger.Enqueue($"Didn't receive active orders for connector {connectorName} in assigned timeout.");
            }
        }

        public async Task WaitForBalance(int timeoutMs, string connectorName)
        {
            try
            {
                await gotBalanceResetEvent.WaitAsync(source.NewPairedTimeoutToken(timeoutMs));
                gotBalanceResetEvent.Reset();
            }
            catch (TaskCanceledException)
            {
                logger.Enqueue($"Didn't receive balance for connector {connectorName} in assigned timeout.");
            }
        }

        public void SetActiveOrdersEvent()
        {
            if (!gotActiveOrdersResetEvent.IsSet) gotActiveOrdersResetEvent.Set();
        }

        public void SetBalanceEvent()
        {
            if (!gotBalanceResetEvent.IsSet) gotBalanceResetEvent.Set();
        }

        public bool TryGetBalance(string currency, out decimal balance)
        {
            return AvailableSpotBalances.TryGetValue(currency, out balance);
        }

        public bool TryUpdateBalance(string currency, decimal balanceDiff, out decimal newBalance)
        {
            newBalance = decimal.MinValue;
            if (!AvailableSpotBalances.TryGetValue(currency, out decimal balance)) return false;

            newBalance = balance + balanceDiff;
            AvailableSpotBalances[currency] = newBalance;
            return true;

        }

        public decimal GetMarginPosDiffAndUpdate(string isin, OrderSide tradeSide, decimal tradeQty)
        {
            marginPositions.TryGetValue(isin, out decimal marginPos);
            decimal oldMarginPos = marginPos;
            decimal newMarginPos = marginPos + (tradeSide == OrderSide.Buy ? tradeQty : -1 * tradeQty);

            marginPositions[isin] = newMarginPos;

            decimal absOldMarginPos = Math.Abs(oldMarginPos);
            decimal absMarginPos = Math.Abs(newMarginPos);
            decimal posAbsDiff = Math.Abs(absMarginPos - absOldMarginPos);

            return absMarginPos >= absOldMarginPos ? posAbsDiff : -1 * posAbsDiff;
        }

        public bool TryGetMarginPosition(string isin, out decimal position)
        {
            return marginPositions.TryGetValue(isin, out position);
        }

        public void UpdateMarginPosition(string isin, decimal position)
        {
            marginPositions[isin] = position;
        }

        public bool IsHedgeExposureLimitOk(LimitMessage limit, out decimal exposureLimitTolerance)
        {
            if (!limitsToStopOnHedgeExposureExceeding.TryGetValue(limit.Name, out exposureLimitTolerance)) return true;
            return limit.Exposure - limit.Min > exposureLimitTolerance && limit.Max - limit.Exposure > exposureLimitTolerance;
        }
    }
}
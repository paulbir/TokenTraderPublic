using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using SharedTools;
using SharedTools.Interfaces;
using TokenTrader.DataStructures;

namespace TokenTrader.State
{
    class IsinVolumeTradingState
    {
        readonly ILogger logger;
        readonly CancellationTokenSource source;

        readonly AsyncManualResetEvent gotAllNewOrdersResponseEvent = new AsyncManualResetEvent();
        readonly AsyncManualResetEvent gotAllExecutionReportsEvent = new AsyncManualResetEvent();
        readonly ConcurrentDictionary<string, byte> sentOrderIdsForNewOrderResponse = new ConcurrentDictionary<string, byte>();
        readonly ConcurrentDictionary<string, byte> sentOrderIdsForExecutionReport = new ConcurrentDictionary<string, byte>();

        readonly StreamWriter tunroverWriter;

        readonly Stopwatch roundtripMeter = Stopwatch.StartNew();

        public string Isin { get; }
        public SortedList<DateTime, decimal> TargetFiatTurnoverPoints { get; set; }
        public PricesState PricesState { get; }
        public PricesState ConversionToFiatPricesState { get; }

        public CircularQueue<TradingSpeed> LastTradingSpeeds { get; set; }
        public decimal MyLastTradePrice { get; set; }

        public DateTime CurrentTurnoverPeriod { get; set; } = DateTime.MinValue;
        public PauseType PauseType { get; set; } = PauseType.None;
        public decimal CurrentTargetTurnoverCrypto { get; set; }
        public decimal CurrentDoneTurnoverCrypto { get; set; }
        public TimeSpan SpanToEndOfPeriod { get; set; }
        public int CurrentTargetDelayMs { get; set; }
        public decimal CurrentTargetQty { get; set; }

        public long OrdersRoundtripMs { get; private set; }
        public bool IsRoundtripMeasuringInProgess => roundtripMeter.IsRunning;

        public string LastSentOrderId { get; set; }
        public bool LastSentOrderExecuted { get; set; }

        public bool IsNarrowSpread { get; set; }

        public IsinVolumeTradingState(string isin,
                                      int lastTradingSpeedsWindowSize,
                                      ILogger logger,
                                      CancellationTokenSource source,
                                      SortedList<DateTime, decimal> targetFiatTurnoverPoints,
                                      StreamWriter tunroverWriter,
                                      PricesState pricesState,
                                      PricesState conversionToFiatPricesState)
        {
            Isin = isin;
            this.logger = logger;
            this.source = source;
            TargetFiatTurnoverPoints = targetFiatTurnoverPoints;
            this.tunroverWriter = tunroverWriter;
            PricesState = pricesState;
            ConversionToFiatPricesState = conversionToFiatPricesState;
            LastTradingSpeeds = new CircularQueue<TradingSpeed>(lastTradingSpeedsWindowSize);
        }

        public async Task WaitForAllNewOrdersResponse(int timeoutMs)
        {
            try
            {
                await gotAllNewOrdersResponseEvent.WaitAsync(source.NewPairedTimeoutToken(timeoutMs));
                gotAllNewOrdersResponseEvent.Reset();
            }
            catch (TaskCanceledException)
            {
                logger.Enqueue($"Didn't receive response for all added orders for isin {Isin} in assigned timeout.");
            }
        }

        public async Task WaitForAllExecutionReports(int timeoutMs)
        {
            try
            {
                await gotAllExecutionReportsEvent.WaitAsync(source.NewPairedTimeoutToken(timeoutMs));
                gotAllExecutionReportsEvent.Reset();
            }
            catch (TaskCanceledException)
            {
                logger.Enqueue($"Didn't receive execution reports for all added orders for isin {Isin} in assigned timeout.");
            }
        }

        public void SetNewOrdersEvent()
        {
            if (!gotAllNewOrdersResponseEvent.IsSet) gotAllNewOrdersResponseEvent.Set();
        }

        public void SetExecutionReportsEvent()
        {
            if (!gotAllExecutionReportsEvent.IsSet) gotAllExecutionReportsEvent.Set();
        }

        public void AddNewOrderId(string id)
        {
            sentOrderIdsForNewOrderResponse.TryAdd(id, 0);
            sentOrderIdsForExecutionReport.TryAdd(id, 0);
        }

        public bool TryRemoveLastNewResponseOrderId(string id)
        {
            return sentOrderIdsForNewOrderResponse.TryRemove(id, out byte dummy) && sentOrderIdsForNewOrderResponse.Count == 0;
        }

        public bool TryRemoveLastExecutionReportOrderId(string id)
        {
            return sentOrderIdsForExecutionReport.TryRemove(id, out byte dummy) && sentOrderIdsForExecutionReport.Count == 0;
        }

        public void ClearNewResponseOrderIds()
        {
            sentOrderIdsForNewOrderResponse.Clear();
        }

        public void ClearSentOrderIds()
        {
            sentOrderIdsForNewOrderResponse.Clear();
            sentOrderIdsForExecutionReport.Clear();
        }

        public int DistributeTurnoverDeviationToNextPeriods(int startIndex, decimal actualTurnoverExcessFiat)
        {
            int i = startIndex;
            bool positive = actualTurnoverExcessFiat > 0;

            while (actualTurnoverExcessFiat != 0 && i < TargetFiatTurnoverPoints.Count)
            {
                decimal targetTurnover = TargetFiatTurnoverPoints.Values[i];

                DateTime key = TargetFiatTurnoverPoints.Keys[i];

                //следующий период увеличивать можем неограниченно, если был недобор.
                //уменьшать только до 0, если был перебор. остаток переносим дальше на следующий период.

                if (positive)
                {
                    if (targetTurnover - actualTurnoverExcessFiat > 0) TargetFiatTurnoverPoints[key] -= actualTurnoverExcessFiat;
                    else TargetFiatTurnoverPoints[key] = 0;

                    decimal nextPeriodDifference = targetTurnover - TargetFiatTurnoverPoints.Values[i];
                    actualTurnoverExcessFiat -= nextPeriodDifference;
                }
                else
                {
                    TargetFiatTurnoverPoints[key] -= actualTurnoverExcessFiat;
                    actualTurnoverExcessFiat = 0;
                }

                i++;
            }

            return i - startIndex;
        }

        public void LogTurnover(decimal thisTradeTurnover, string buyPublicKey, string sellPublicKey)
        {
            tunroverWriter.WriteLine($"{DateTime.UtcNow:HH:mm:ss};" +
                                     $"{(Math.Round(thisTradeTurnover * 100) / 100).ToString(CultureInfo.InvariantCulture).Replace('.', ',')};" +
                                     $"{buyPublicKey};{sellPublicKey}");
            tunroverWriter.Flush();
        }

        public void StartMeasuringRoundtrip()
        {
            roundtripMeter.Start();
        }

        public void PauseMeasuringRoundtrip()
        {
            roundtripMeter.Stop();
        }

        public void ResumeMeasuringRoundtrip()
        {
            roundtripMeter.Start();
        }

        public void StoreRoundtrip()
        {
            OrdersRoundtripMs = roundtripMeter.ElapsedMilliseconds;
            roundtripMeter.Reset();
        }

        public void ClearLastSentOrderData()
        {
            LastSentOrderId = "";
            LastSentOrderExecuted = false;
        }
    }
}
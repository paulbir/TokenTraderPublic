using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using SharedTools.Interfaces;
using Timer = System.Timers.Timer;

namespace SharedTools
{
    public class Logger : ILogger, IDisposable
    {
        const string Format = "HH:mm:ss.fff";
        const long MicrosecondsInSecond = 1000000;

        readonly bool appendLog;
        readonly int bufferSize;
        readonly bool dumpLoggingEnabled;
        readonly bool isData;
        readonly object logLocker = new object();
        readonly ConcurrentQueue<string> messageQueue;
        readonly string output;
        readonly Timer timer;
        readonly DateTimePrecise dtPrecise = new DateTimePrecise();

        long counter;
        bool fDumpInProgress;
        double prevTimestamp;
        int secondPartDumpedCounter;
        StreamWriter sw;

        long meanDelay;
        long stdevDelay;
        int numPackets;
        long meanDelayNegative;
        long stdevDelayNegative;
        int numPacketsNegative;
        readonly Stopwatch delayStopwatch = Stopwatch.StartNew();
        readonly Stopwatch mainStopWatch = Stopwatch.StartNew();

        public Logger(string output = "applog.txt", bool appendLog = false, bool isData = false, bool dumpLoggingEnabled = false, int bufferSize = 0)
        {
            Contract.Requires(!string.IsNullOrEmpty(output) && output.IndexOfAny(Path.GetInvalidPathChars()) < 0,
                                                 "output is empty or contains invalid characters");

            this.isData = isData;
            this.dumpLoggingEnabled = dumpLoggingEnabled;
            this.output = output;
            this.appendLog = appendLog;
            this.bufferSize = bufferSize;
            secondPartDumpedCounter = 0;
            fDumpInProgress = false;
            messageQueue = new ConcurrentQueue<string>();

            timer = new Timer(3000);
            timer.Elapsed += Timer_Elapsed;

            SetOutput();

            //if (!Debugger.IsAttached && !dumpLoggingEnabled)
            //{
            //    timer.Start();
            //}
            Task.Run(() =>
                     {
                         Stopwatch localStopwatch = Stopwatch.StartNew();
                         while (localStopwatch.ElapsedMilliseconds < 10000)
                         {
                             DateTime now = dtPrecise.UtcNow;
                             Task.Delay(1);
                         }

                         localStopwatch.Stop();
                         messageQueue.Enqueue($"{counter};{dtPrecise.Now.ToString(Format)};Finished synchronizing precise timer.");
                     });

            timer.Start();
        }

        public void Enqueue(string str)
        {
            if (dumpLoggingEnabled)
            {
                lock (logLocker)
                {
                    if (messageQueue.Count >= bufferSize)
                    {
                        messageQueue.TryDequeue(out _);
                    }

                    if (fDumpInProgress) //-V3054
                    {
                        if (secondPartDumpedCounter > bufferSize) //-V3054
                        {
                            fDumpInProgress = false;
                            secondPartDumpedCounter = 0;
                            WriteQueueToOutput();
                        }
                        else
                        {
                            secondPartDumpedCounter++;
                        }
                    }
                }
            }

            lock (logLocker)
            {
                counter++;
                long timeDiff = mainStopWatch.ElapsedTicks * MicrosecondsInSecond / Stopwatch.Frequency;

                string tmpStr = $"{counter};{dtPrecise.Now.ToString(Format)};{timeDiff.ToString().PadRight(10)};{Thread.CurrentThread.ManagedThreadId.ToString().PadRight(3)};{str}";
                messageQueue.Enqueue(tmpStr);
                mainStopWatch.Restart();
            }
        }

        public void EnqueueDelay(DateTime sendingTime)
        {
            lock (logLocker)
            {
                counter++;
                double timestamp = mainStopWatch.ElapsedTicks;
                double timeDiff;

                if (Math.Abs(prevTimestamp) > 0.0000001d) timeDiff = timestamp - prevTimestamp;
                else timeDiff = 0;

                DateTime now = dtPrecise.Now;
                long delay = (now - sendingTime).Ticks / 10;

                if (delay >= 0)
                {
                    numPackets++;
                    meanDelay += delay;
                    stdevDelay += delay * delay;
                }
                else
                {
                    numPacketsNegative++;
                    meanDelayNegative += delay;
                    stdevDelayNegative += delay * delay;
                }

                if (delayStopwatch.ElapsedMilliseconds >= 5000 && numPackets > 1)
                {
                    meanDelay /= numPackets;
                    stdevDelay = (long)Math.Sqrt((double)(stdevDelay - meanDelay * meanDelay * numPackets) / (numPackets - 1));

                    if (numPacketsNegative > 1)
                    {
                        meanDelayNegative /= numPacketsNegative;
                        stdevDelayNegative = (long)Math.Sqrt((double)(stdevDelayNegative - meanDelayNegative * meanDelayNegative * numPacketsNegative) / (numPacketsNegative - 1));
                    }
                    else
                    {
                        meanDelayNegative = 0;
                        stdevDelayNegative = 0;
                    }

                    string tmpStr = $"{counter};{now.ToString(Format)};{Math.Round(timeDiff)};{meanDelay};{stdevDelay};{numPackets};{meanDelayNegative};{stdevDelayNegative};{numPacketsNegative}";
                    prevTimestamp = timestamp;
                    messageQueue.Enqueue(tmpStr);

                    numPackets = 0;
                    meanDelay = 0;
                    stdevDelay = 0;

                    numPacketsNegative = 0;
                    meanDelayNegative = 0;
                    stdevDelayNegative = 0;
                    delayStopwatch.Restart();
                }
            }
        }

        public void WriteQueueToOutput()
        {
            string str;
            string timestampStr = "";

            if (dumpLoggingEnabled) timestampStr = dtPrecise.Now.ToString(Format);

            if (isData)
            {
                while (messageQueue.TryDequeue(out str)) sw.WriteLine(timestampStr + str);
                sw.Flush();
            }
            else
            {
                while (messageQueue.TryDequeue(out str)) Trace.WriteLine(timestampStr + str);
                Trace.Flush();
            }
        }

        public void WriteToOutput(string str)
        {
            if (isData)
            {
                sw.WriteLine(dtPrecise.Now.ToString(Format)+ ";" + str);
                sw.Flush();
            }
            else
            {
                Trace.WriteLine(dtPrecise.Now.ToString(Format) + ";" + str);
                Trace.Flush();
            }
        }

        public void Dump()
        {
            fDumpInProgress = true;
            WriteQueueToOutput();
            secondPartDumpedCounter = 0;
        }

        void SetOutput()
        {
            if (isData) sw = new StreamWriter(output, appendLog);
            else
            {
                if (output != "console")
                {
                    FileStream log;
                    if (!appendLog && File.Exists(output)) log = new FileStream(output, FileMode.Truncate);
                    else log = new FileStream(output, FileMode.Append);

                    Trace.Listeners.Add(new TextWriterTraceListener(log));
                }
                else
                {
                    throw new NotImplementedException("ConsoleTraceLisnerer is not supported in .Net Core, so it is not implemented yet.");
                }
            }
        }

        void Timer_Elapsed(object source, ElapsedEventArgs e)
        {
            timer.Stop();
            WriteQueueToOutput();
            timer.Start();
        }

        public void Dispose()
        {
            timer?.Dispose();
            sw?.Dispose();
        }
    }
}
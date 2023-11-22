using System;
using System.Diagnostics;

namespace SharedTools
{
    /// DateTimePrecise class in C# -- an improvement to DateTime.Now
    /// By jamesdbrock
    /// http://www.codeproject.com/KB/cs/DateTimePrecise.aspx
    /// Licensed via The Code Project Open License (CPOL) 1.02
    /// http://www.codeproject.com/info/cpol10.aspx
    /// 
    /// DateTimePrecise provides a way to get a DateTime that exhibits the
    /// relative precision of
    /// System.Diagnostics.Stopwatch, and the absolute accuracy of DateTime.Now.
    public class DateTimePrecise
    {
        const long TicksInSecond = 10000000;

        readonly Stopwatch stopwatch;
        readonly long synchronizePeriodStopwatchTicks;
        DateTimePreciseSafeImmutable immutableDateTime;

        /// Returns the current date and time, just like DateTime.UtcNow.
        public DateTime UtcNow
        {
            get
            {
                long elapsedTicks = stopwatch.ElapsedTicks;

                long ticksToAdd;
                if (elapsedTicks < immutableDateTime.TicksObserved + synchronizePeriodStopwatchTicks)
                {
                    ticksToAdd = (elapsedTicks - immutableDateTime.TicksObserved) * TicksInSecond / immutableDateTime.StopWatchFrequency;
                    return immutableDateTime.TimeBase.AddTicks(ticksToAdd);
                }

                DateTime now = DateTime.UtcNow;

                ticksToAdd = (elapsedTicks - immutableDateTime.TicksObserved) * TicksInSecond / immutableDateTime.StopWatchFrequency;
                DateTime timeBaseNew = immutableDateTime.TimeBase.AddTicks(ticksToAdd);

                long newFrequency = (elapsedTicks - immutableDateTime.TicksObserved) * TicksInSecond * 2 /
                                    (now.Ticks - immutableDateTime.TimeObserved.Ticks + now.Ticks + now.Ticks - timeBaseNew.Ticks -
                                     immutableDateTime.TimeObserved.Ticks);

                immutableDateTime = new DateTimePreciseSafeImmutable(now, timeBaseNew, elapsedTicks, newFrequency);

                return timeBaseNew;
            }
        }

        /// Returns the current date and time, just like DateTime.Now.
        public DateTime Now => UtcNow.ToLocalTime();

        /// Creates a new instance of DateTimePrecise.
        /// A large value of synchronizePeriodSeconds may cause arithmetic overthrow
        /// exceptions to be thrown. A small value may cause the time to be unstable.
        /// A good value is 10.
        /// synchronizePeriodSeconds = The number of seconds after which the
        /// DateTimePrecise will synchronize itself with the system clock.
        public DateTimePrecise(long synchronizePeriodSeconds = 10)
        {
            stopwatch = Stopwatch.StartNew();
            stopwatch.Start();

            DateTime now = DateTime.UtcNow;
            immutableDateTime = new DateTimePreciseSafeImmutable(now, now, stopwatch.ElapsedTicks, Stopwatch.Frequency);

            synchronizePeriodStopwatchTicks = synchronizePeriodSeconds * Stopwatch.Frequency;
        }
    }

    sealed class DateTimePreciseSafeImmutable
    {
        internal DateTime TimeObserved { get; }
        internal DateTime TimeBase { get; }
        internal long TicksObserved { get; }
        internal long StopWatchFrequency { get; }

        internal DateTimePreciseSafeImmutable(DateTime timeObserved, DateTime timeBase, long ticksObserved, long stopWatchFrequency)
        {
            TimeObserved = timeObserved;
            TimeBase = timeBase;
            TicksObserved = ticksObserved;
            StopWatchFrequency = stopWatchFrequency;
        }
    }
}
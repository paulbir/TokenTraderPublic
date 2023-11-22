using System;
using System.Threading;

namespace SharedTools
{
    public static class ThreadSafeRandom
    {
        [ThreadStatic]
        static Random local;

        public static Random ThisThreadsRandom => local ?? (local = new Random(unchecked(Environment.TickCount * 31 + Thread.CurrentThread.ManagedThreadId)));
    }
}
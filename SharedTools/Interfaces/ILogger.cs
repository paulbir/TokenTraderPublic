using System;

namespace SharedTools.Interfaces
{
    public interface ILogger
    {
        void Enqueue(string str);
        void EnqueueDelay(DateTime sendingTime);
        void WriteQueueToOutput();
        void WriteToOutput(string str);
        void Dump();
    }
}
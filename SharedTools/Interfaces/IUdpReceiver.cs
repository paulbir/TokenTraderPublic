using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace SharedTools.Interfaces
{
    public interface IUdpReceiver
    {
        void Initialize(int port, ActionBlock<string> processorBlock, CancellationToken token);
        Task Receive();
    }
}
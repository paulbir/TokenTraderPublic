using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using SharedTools.Interfaces;

namespace SharedTools
{
    public class UdpReceiver : IUdpReceiver
    {
        UdpClient  client;
        ActionBlock<string> processorBlock;
        CancellationToken   token;

        public void Initialize(int port, ActionBlock<string> processorBlockP, CancellationToken tokenP)
        {
            client = new UdpClient(port);
            processorBlock = processorBlockP;
            token          = tokenP;
        }

        public async Task Receive()
        {
            while (!token.IsCancellationRequested)
            {
                UdpReceiveResult result  = await client.ReceiveAsync();
                string           message = Encoding.ASCII.GetString(result.Buffer);
                await processorBlock.SendAsync(message, token);
            }
        }
    }
}
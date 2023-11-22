using System.Net.Sockets;
using System.Text;
using SharedTools.Interfaces;

namespace SharedTools
{
    public class UdpSender : IUdpSender
    {
        UdpClient client;
        readonly object    locker = new object();

        public UdpSender(){}

        //public UdpSender(int port)
        //{
        //    client = new UdpClient();
        //    client.Connect("127.0.255.255", port);
        //}

        public void Initialize(int port)
        {
            client = new UdpClient();
            client.Connect("127.0.255.255", port);
        }

        public void SendMessage(string message)
        {
            byte[] byteMsg = Encoding.ASCII.GetBytes(message);

            lock (locker) client.Send(byteMsg, byteMsg.Length);
        }

        public void Stop()
        {
            lock (locker) client?.Close();
        }
    }
}
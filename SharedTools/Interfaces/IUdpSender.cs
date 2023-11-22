namespace SharedTools.Interfaces
{
    public interface IUdpSender
    {
        void Initialize(int port);
        void SendMessage(string message);
    }
}
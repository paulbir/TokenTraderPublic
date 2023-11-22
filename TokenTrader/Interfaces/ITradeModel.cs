using System.Threading.Tasks;

namespace TokenTrader.Interfaces
{
    interface ITradeModel
    {
        Task Start();
        void Stop(bool isEmergencyStop, string errorDescription = null);
    }
}
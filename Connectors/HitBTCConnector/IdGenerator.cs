using SharedTools.Interfaces;

namespace HitBTCConnector
{
    class IdGenerator : IIdGenerator
    {
        int id;
        readonly object locker = new object();
        public int Id
        {
            get
            {
                lock(locker)
                {
                    return id++;
                }
            }
        }
}
}

using SharedTools.Interfaces;

namespace SharedTools
{
    public class IdGeneratorOneBased : IIdGenerator
    {
        int id = 1;
        readonly object locker = new object();

        public int Id
        {
            get
            {
                lock (locker)
                {
                    return id++;
                }
            }
        }
    }
}
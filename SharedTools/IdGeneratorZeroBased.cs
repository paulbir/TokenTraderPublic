using SharedTools.Interfaces;

namespace SharedTools
{
    public class IdGeneratorZeroBased : IIdGenerator
    {
        int             id;
        readonly object locker = new object();

        public int Id
        {
            get
            {
                lock (locker) { return id++; }
            }
        }
    }
}
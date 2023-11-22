using SharedTools.Interfaces;

namespace SharedTools
{
    public class IdGeneratorDecrement : IIdGenerator
    {
        int             id     = int.MaxValue;
        readonly object locker = new object();

        public int Id
        {
            get
            {
                lock (locker) { return id--; }
            }
        }
    }
}
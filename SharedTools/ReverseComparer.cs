using System.Collections.Generic;

namespace SharedTools
{
    public class ReverseComparer<T> : IComparer<T>
    {
        readonly IComparer<T> inner;

        public ReverseComparer()
            : this(null) {}

        public ReverseComparer(IComparer<T> inner)
        {
            this.inner = inner ?? Comparer<T>.Default;
        }

        int IComparer<T>.Compare(T x, T y)
        {
            return inner.Compare(y, x);
        }
    }
}
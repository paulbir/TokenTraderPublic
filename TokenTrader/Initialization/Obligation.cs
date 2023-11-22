using System;
using SharedDataStructures.Exceptions;

namespace TokenTrader.Initialization
{
    class Obligation : IComparable<Obligation>
    {
        public decimal SpreadOneSidePerc { get; set; }
        public decimal VolumeOneSideFiat     { get; set; }

        public int CompareTo(Obligation other)
        {
            // A null value means that this object is greater.
            return other == null ? 1 : SpreadOneSidePerc.CompareTo(other.SpreadOneSidePerc);
        }

        public void Verify()
        {
            if (SpreadOneSidePerc <= 0) throw new ConfigErrorsException("SpreadOneSidePerc in Obligation was not set properly.");
            if (VolumeOneSideFiat <= 0) throw new ConfigErrorsException("VolumeOneSideFiat in Obligation was not set properly.");
        }
    }
}
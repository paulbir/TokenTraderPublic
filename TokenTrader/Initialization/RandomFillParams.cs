using SharedDataStructures.Exceptions;

namespace TokenTrader.Initialization 
{
    class RandomFillParams 
    {
        public int VolumeOneSideFiat { get; set; }
        public int OrderVolumeMuFiat { get; set; }
        public decimal OrderQtySigmaFrac { get; set; }
        public int OrdersDistanceMuMinsteps { get; set; }
        public int MinSpreadOneSideMinsteps { get; set; }
        public bool? ChangeDeepOrders { get; set; }

        public void Verify()
        {
            if (VolumeOneSideFiat         <= 0) throw new ConfigErrorsException("VolumeOneSideFiat in RandomFill was not set properly.");
            if (OrderVolumeMuFiat         <= 0) throw new ConfigErrorsException("OrderVolumeMuFiat in RandomFill was not set properly.");
            if (OrderQtySigmaFrac         <= 0) throw new ConfigErrorsException("OrderQtySigmaFrac in RandomFill was not set properly.");
            if (OrdersDistanceMuMinsteps  <= 0) throw new ConfigErrorsException("OrdersDistanceMuMinsteps in RandomFill was not set properly.");
            if (MinSpreadOneSideMinsteps  <= 0) throw new ConfigErrorsException("MinSpreadOneSideMinsteps in RandomFill was not set properly.");
            if (ChangeDeepOrders == null) throw new ConfigErrorsException("ChangeDeepOrders in RandomFill was not set properly.");
        }
    }
}
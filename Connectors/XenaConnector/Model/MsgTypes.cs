namespace XenaConnector.Model
{
    static class MsgTypes
    {
        public const string AccountStatusSnapshot = "XAR";
        public const string AccountStatusUpdate = "XAF";
        public const string AccountStatusReportRequest = "XAA";
        public const string ExecutionReport = "8";
        public const string HeartBeat = "0";
        public const string Logon = "A";
        public const string MassPositionReport = "MAP";
        public const string MktDataRequest = "V";
        public const string MktDataSnapshot = "W";
        public const string MktDataUpdate = "X";
        public const string NewOrder = "D";
        public const string OrderMassStatusRequest = "AF";
        public const string OrderMassStatusResponse = "U8";
        public const string OrderCancelReject = "9";
        public const string OrderCancelRequest = "F";
        public const string PositionMaintenanceRequest = "AL";
        public const string PositionReport = "AP";
        public const string PositionReportRequest = "AN";
        public const string Reject = "3";
        public const string TestRequest = "1";
    }
}
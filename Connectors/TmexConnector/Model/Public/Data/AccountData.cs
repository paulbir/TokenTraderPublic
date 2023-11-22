using System.Runtime.Serialization;

namespace TmexConnector.Model.Public.Data
{
    /// <summary>
    /// Universal structure for accounts/positions
    /// </summary>
    [DataContract]
    public class AccountData
    {
        /// <summary>
        /// Symbol
        /// </summary>
        [DataMember(Name = "s", EmitDefaultValue = false)]
        public string Symbol { get; set; }

        /// <summary>
        /// Portfolio id.
        /// If =0 - aggregated data for account
        /// </summary>
        [DataMember(Name = "pi", EmitDefaultValue = false)]
        public long PortfolioId { get; set; }

        /// <summary>
        /// Current volume
        /// </summary>
        [DataMember(Name = "v", EmitDefaultValue = false)]
        public decimal CurrentVolume { get; set; }

        /// <summary>
        /// Locked margin
        /// </summary>
        [DataMember(Name = "m", EmitDefaultValue = false)]
        public decimal LockedMargin { get; set; }

        /// <summary>
        /// Orders margin
        /// </summary>
        [DataMember(Name = "im", EmitDefaultValue = false)]
        public decimal InitialMargin { get; set; }

        /// <summary>
        /// Maintenance margin
        /// </summary>
        [DataMember(Name = "mm", EmitDefaultValue = false)]
        public decimal MaintenanceMargin { get; set; }

        /// <summary>
        /// Average price
        /// </summary>
        [DataMember(Name = "p", EmitDefaultValue = false)]
        public decimal AvgPrice { get; set; }

        /// <summary>
        /// Mark price
        /// </summary>
        [DataMember(Name = "mp", EmitDefaultValue = false)]
        public decimal MarkPrice { get; set; }

        /// <summary>
        /// Is payment?
        /// If false - this is a position, otherwise - account
        /// </summary>
        [DataMember(Name = "c", EmitDefaultValue = false)]
        public bool IsPayment { get; set; }

        /// <summary>
        /// Timestamp
        /// </summary>
        [DataMember(Name = "t", EmitDefaultValue = false)]
        public long Timestamp { get; set; }
    }
}
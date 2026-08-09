using System;

namespace Sayra.Backend.Domain
{
    public class Workstation : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public string Status { get; set; } = "Offline"; // e.g. Offline, Online, InUse, Maintenance
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
        public byte[]? VerificationPublicKey { get; set; }

        // Optimistic concurrency token
        public uint RowVersion { get; set; }
    }
}

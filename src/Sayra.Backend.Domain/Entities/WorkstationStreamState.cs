using System;

namespace Sayra.Backend.Domain.Entities
{
    public class WorkstationStreamState : BaseEntity
    {
        public string ClientId { get; set; } = string.Empty;
        public Guid? WorkstationId { get; set; }
        public long LastSequenceNumber { get; set; }
        public DateTime? LastOccurredAt { get; set; }
        public DateTime LastProcessedAt { get; set; } = DateTime.UtcNow;
        public byte[]? RowVersion { get; set; }
    }
}

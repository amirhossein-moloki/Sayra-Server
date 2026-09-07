using System;
using Sayra.Backend.Domain.Telemetry;

namespace Sayra.Backend.Domain.Entities
{
    public class HeartbeatHistoryRecord : BaseEntity
    {
        public Guid WorkstationId { get; set; }
        public Guid? OrganizationId { get; set; }
        public Guid? SiteId { get; set; }
        public string PcId { get; set; } = string.Empty;
        public string? ConnectionId { get; set; }
        public DateTime ClientTimestamp { get; set; }
        public DateTime ServerReceivedAt { get; set; }
        public DateTime ProcessedAt { get; set; }

        public HeartbeatHistoryRecord()
        {
        }

        public static HeartbeatHistoryRecord FromSignal(
            HeartbeatSignal signal,
            string? connectionId = null)
        {
            if (signal == null)
            {
                throw new ArgumentNullException(nameof(signal));
            }

            return new HeartbeatHistoryRecord
            {
                WorkstationId = signal.Identity.WorkstationId ?? Guid.Empty,
                OrganizationId = signal.Identity.OrganizationId,
                SiteId = signal.Identity.SiteId,
                PcId = signal.Identity.PcId,
                ConnectionId = connectionId,
                ClientTimestamp = signal.ClientTimestamp,
                ServerReceivedAt = signal.ServerReceivedAt,
                ProcessedAt = signal.ProcessedAt
            };
        }
    }
}

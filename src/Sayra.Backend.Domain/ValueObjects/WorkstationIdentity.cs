using System;

namespace Sayra.Backend.Domain.ValueObjects
{
    public sealed class WorkstationIdentity : IEquatable<WorkstationIdentity>
    {
        public string PcId { get; }
        public Guid? WorkstationId { get; }
        public Guid? SiteId { get; }
        public Guid? OrganizationId { get; }

        public WorkstationIdentity(string pcId, Guid? workstationId = null, Guid? siteId = null, Guid? organizationId = null)
        {
            if (string.IsNullOrWhiteSpace(pcId))
            {
                throw new ArgumentException("PC-ID cannot be null or empty.", nameof(pcId));
            }

            PcId = pcId.Trim().ToUpperInvariant();
            WorkstationId = workstationId;
            SiteId = siteId;
            OrganizationId = organizationId;
        }

        public bool Matches(string? candidatePcId)
        {
            if (string.IsNullOrWhiteSpace(candidatePcId))
            {
                return true; // Empty candidate payload PC-ID is not a mismatch
            }

            return string.Equals(PcId, candidatePcId.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public bool Equals(WorkstationIdentity? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(PcId, other.PcId, StringComparison.OrdinalIgnoreCase) &&
                   WorkstationId == other.WorkstationId &&
                   SiteId == other.SiteId &&
                   OrganizationId == other.OrganizationId;
        }

        public override bool Equals(object? obj) => Equals(obj as WorkstationIdentity);

        public override int GetHashCode() => HashCode.Combine(PcId, WorkstationId, SiteId, OrganizationId);

        public override string ToString() => $"WorkstationIdentity({PcId}, WorkstationId: {WorkstationId})";
    }
}

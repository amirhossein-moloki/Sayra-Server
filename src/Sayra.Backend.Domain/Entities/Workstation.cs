using System;
using System.Text.RegularExpressions;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class Workstation : BaseEntity
    {
        // Cached compiled Regex for MAC address validation to avoid per-call heap allocation and runtime regex compilation.
        private static readonly Regex MacRegex = new Regex(@"^([0-9A-F]{2}:){5}[0-9A-F]{2}$", RegexOptions.Compiled);

        public string Name { get; set; } = string.Empty;
        public string PcId { get; set; } = string.Empty;
        public string SiteId { get; set; } = string.Empty;
        public Guid? OrganizationEntityId { get; set; }
        public Guid? SiteEntityId { get; set; }
        public Guid? ZoneEntityId { get; set; }
        public string Hostname { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public string ClientVersion { get; set; } = string.Empty;
        public string OsVersion { get; set; } = string.Empty;
        public string Status { get; set; } = "OFFLINE"; // Supported: UNKNOWN, OFFLINE, ONLINE, LOCKED, IN_USE, MAINTENANCE
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
        public byte[]? VerificationPublicKey { get; set; }
        public bool IsDisabled { get; set; }
        public bool IsDeactivated { get; set; }
        public bool IsProvisioned { get; set; }
        public DateTime? ProvisionedAt { get; set; }

        // Optimistic concurrency token
        public uint RowVersion { get; set; }

        public void TransitionTo(string newStatus)
        {
            var target = (newStatus ?? string.Empty).Trim().ToUpperInvariant();
            var current = (Status ?? string.Empty).Trim().ToUpperInvariant();

            if (target == current) return;

            if (target != "UNKNOWN" && target != "OFFLINE" && target != "ONLINE" && target != "LOCKED" && target != "IN_USE" && target != "MAINTENANCE")
            {
                throw new InvalidDomainException("INVALID_STATUS", $"Invalid status: {newStatus}");
            }

            bool isValid = false;

            switch (current)
            {
                case "UNKNOWN":
                    isValid = (target == "OFFLINE");
                    break;
                case "OFFLINE":
                    isValid = (target == "ONLINE");
                    break;
                case "ONLINE":
                    isValid = (target == "IN_USE" || target == "LOCKED" || target == "MAINTENANCE" || target == "OFFLINE");
                    break;
                case "IN_USE":
                    isValid = (target == "ONLINE" || target == "MAINTENANCE" || target == "OFFLINE");
                    break;
                case "LOCKED":
                    isValid = (target == "ONLINE" || target == "OFFLINE");
                    break;
                case "MAINTENANCE":
                    isValid = (target == "ONLINE" || target == "OFFLINE");
                    break;
                default:
                    // If stored state was some custom string, allow transitioning to OFFLINE to recover
                    isValid = (target == "OFFLINE");
                    break;
            }

            if (!isValid)
            {
                throw new InvalidDomainException("INVALID_TRANSITION", $"Cannot transition directly from {current} to {target}.");
            }

            Status = target;
            UpdatedAt = DateTime.UtcNow;
        }

        public void NormalizeAndValidate()
        {
            // Normalize PC ID
            if (string.IsNullOrWhiteSpace(PcId))
            {
                throw new InvalidDomainException("INVALID_PC_ID", "PcId is required and cannot be empty.");
            }
            PcId = PcId.Trim().ToUpperInvariant();

            // Name should align with PcId if empty
            if (string.IsNullOrWhiteSpace(Name))
            {
                Name = PcId;
            }
            Name = Name.Trim();

            // Normalize Site ID
            if (string.IsNullOrWhiteSpace(SiteId))
            {
                throw new InvalidDomainException("INVALID_SITE_ID", "SiteId is required and cannot be empty.");
            }
            SiteId = SiteId.Trim().ToUpperInvariant();

            // Normalize Hostname
            if (string.IsNullOrWhiteSpace(Hostname))
            {
                throw new InvalidDomainException("INVALID_HOSTNAME", "Hostname is required.");
            }
            Hostname = Hostname.Trim();

            // Validate and normalize MAC Address
            if (string.IsNullOrWhiteSpace(MacAddress))
            {
                throw new InvalidDomainException("INVALID_MAC_ADDRESS", "MAC Address is required.");
            }
            MacAddress = MacAddress.Trim().ToUpperInvariant().Replace("-", ":");
            // Standard MAC validation regex: 6 octets separated by colons
            if (!MacRegex.IsMatch(MacAddress))
            {
                throw new InvalidDomainException("INVALID_MAC_ADDRESS", "MAC Address format is invalid.");
            }

            // Validate IP Address
            if (string.IsNullOrWhiteSpace(IpAddress))
            {
                throw new InvalidDomainException("INVALID_IP_ADDRESS", "IP Address is required.");
            }
            IpAddress = IpAddress.Trim();
            if (!System.Net.IPAddress.TryParse(IpAddress, out _))
            {
                throw new InvalidDomainException("INVALID_IP_ADDRESS", "IP Address format is invalid.");
            }

            // Version metadata can be empty but should be normalized
            ClientVersion = (ClientVersion ?? string.Empty).Trim();
            OsVersion = (OsVersion ?? string.Empty).Trim();
        }
    }
}

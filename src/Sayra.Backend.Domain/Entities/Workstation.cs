using System;
using System.Text.RegularExpressions;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class Workstation : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string PcId { get; set; } = string.Empty;
        public string SiteId { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public string ClientVersion { get; set; } = string.Empty;
        public string OsVersion { get; set; } = string.Empty;
        public string Status { get; set; } = "Offline"; // e.g. Offline, Online, InUse, Maintenance
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
        public byte[]? VerificationPublicKey { get; set; }
        public bool IsDisabled { get; set; }

        // Optimistic concurrency token
        public uint RowVersion { get; set; }

        public void TransitionTo(string newStatus)
        {
            if (newStatus == Status) return;

            if (newStatus != "Offline" && newStatus != "Online" && newStatus != "InUse" && newStatus != "Maintenance")
            {
                throw new InvalidDomainException("INVALID_STATUS", $"Invalid status: {newStatus}");
            }

            if (Status == "Offline" && newStatus == "InUse")
            {
                throw new InvalidDomainException("INVALID_TRANSITION", "Cannot transition directly from Offline to InUse. Workstation must be Online first.");
            }

            if (Status == "Maintenance" && newStatus == "InUse")
            {
                throw new InvalidDomainException("INVALID_TRANSITION", "Cannot transition directly from Maintenance to InUse. Workstation must be Online first.");
            }

            Status = newStatus;
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
            var macRegex = new Regex(@"^([0-9A-F]{2}:){5}[0-9A-F]{2}$");
            if (!macRegex.IsMatch(MacAddress))
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

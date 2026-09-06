using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Contracts;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public class UpdateManifestRequest
    {
        public UserPrincipal Principal { get; set; } = null!;
        public string? ReportedVersion { get; set; }
        public string? OsVersion { get; set; }
        public string? Architecture { get; set; }
        public string? DownloadBaseUrl { get; set; }
    }

    public class UpdateManifestResult
    {
        public bool UpdateAvailable { get; set; }
        public ClientUpdateManifestContract? Manifest { get; set; }
        public string ReasonCode { get; set; } = EligibilityReasonCodes.NoUpdateAvailable;
        public string ReasonDetails { get; set; } = string.Empty;

        public static UpdateManifestResult Available(ClientUpdateManifestContract manifest, string reasonDetails = "Update available.")
        {
            return new UpdateManifestResult
            {
                UpdateAvailable = true,
                Manifest = manifest,
                ReasonCode = EligibilityReasonCodes.Eligible,
                ReasonDetails = reasonDetails
            };
        }

        public static UpdateManifestResult NotAvailable(string reasonCode, string reasonDetails)
        {
            return new UpdateManifestResult
            {
                UpdateAvailable = false,
                Manifest = null,
                ReasonCode = reasonCode,
                ReasonDetails = reasonDetails
            };
        }
    }

    public interface IUpdateManifestService
    {
        Task<UpdateManifestResult> GetManifestAsync(
            UpdateManifestRequest request,
            CancellationToken cancellationToken = default);
    }
}

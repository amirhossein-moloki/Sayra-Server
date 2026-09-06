using System;
using System.Collections.Generic;
using Sayra.Backend.Contracts;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public static class EligibilityReasonCodes
    {
        public const string Eligible = "ELIGIBLE";
        public const string NoUpdateAvailable = "NO_UPDATE_AVAILABLE";
        public const string ClientAlreadyCurrent = "CLIENT_ALREADY_CURRENT";
        public const string ClientVersionIncompatible = "CLIENT_VERSION_INCOMPATIBLE";
        public const string OsIncompatible = "OS_INCOMPATIBLE";
        public const string ArchitectureIncompatible = "ARCHITECTURE_INCOMPATIBLE";
        public const string NoTargetMatch = "NO_TARGET_MATCH";
        public const string RolloutNotEligible = "ROLLOUT_NOT_ELIGIBLE";
        public const string ReleaseNotActive = "RELEASE_NOT_ACTIVE";
        public const string ReleaseRevoked = "RELEASE_REVOKED";
        public const string PackageNotReady = "PACKAGE_NOT_READY";
        public const string SignatureInvalid = "SIGNATURE_INVALID";
        public const string MinimumVersionEnforced = "MINIMUM_VERSION_ENFORCED";
        public const string RollbackNotAllowed = "ROLLBACK_NOT_ALLOWED";
        public const string OrganizationMismatch = "ORGANIZATION_MISMATCH";
        public const string WorkstationNotFound = "WORKSTATION_NOT_FOUND";
        public const string WorkstationDeactivated = "WORKSTATION_DEACTIVATED";
    }

    public class ClientEvaluationContext
    {
        public Guid OrganizationId { get; set; }
        public Guid WorkstationId { get; set; }
        public string PcId { get; set; } = string.Empty;
        public Guid? SiteId { get; set; }
        public List<Guid> GroupIds { get; set; } = new List<Guid>();
        public string CurrentVersion { get; set; } = string.Empty;
        public string? OsVersion { get; set; }
        public string? Architecture { get; set; }
    }

    public class UpdateEligibilityResult
    {
        public bool IsEligible { get; set; }
        public Guid? SelectedReleaseId { get; set; }
        public Guid? SelectedPackageId { get; set; }
        public string TargetVersion { get; set; } = string.Empty;
        public string ReasonCode { get; set; } = EligibilityReasonCodes.NoUpdateAvailable;
        public string ReasonDetails { get; set; } = string.Empty;
        public int RolloutBucket { get; set; }
        public int RolloutPercentage { get; set; } = 100;
        public bool IsMandatory { get; set; }
        public string? MinimumSupportedVersion { get; set; }
        public ClientUpdateReleaseContract? ReleaseContract { get; set; }
        public ClientUpdatePackageMetadataContract? PackageContract { get; set; }

        public static UpdateEligibilityResult Eligible(
            ClientUpdateReleaseContract releaseContract,
            ClientUpdatePackageMetadataContract packageContract,
            int rolloutBucket,
            int rolloutPercentage,
            bool isMandatory = false,
            string? minimumSupportedVersion = null,
            string reasonDetails = "Client is eligible for this update release.")
        {
            return new UpdateEligibilityResult
            {
                IsEligible = true,
                SelectedReleaseId = releaseContract.ReleaseId,
                SelectedPackageId = packageContract.PackageId,
                TargetVersion = releaseContract.Version,
                ReasonCode = EligibilityReasonCodes.Eligible,
                ReasonDetails = reasonDetails,
                RolloutBucket = rolloutBucket,
                RolloutPercentage = rolloutPercentage,
                IsMandatory = isMandatory,
                MinimumSupportedVersion = minimumSupportedVersion,
                ReleaseContract = releaseContract,
                PackageContract = packageContract
            };
        }

        public static UpdateEligibilityResult Ineligible(
            string reasonCode,
            string reasonDetails,
            int rolloutBucket = 0,
            string? targetVersion = null)
        {
            return new UpdateEligibilityResult
            {
                IsEligible = false,
                ReasonCode = reasonCode,
                ReasonDetails = reasonDetails,
                RolloutBucket = rolloutBucket,
                TargetVersion = targetVersion ?? string.Empty
            };
        }
    }
}

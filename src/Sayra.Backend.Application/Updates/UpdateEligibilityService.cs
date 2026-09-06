using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public class UpdateEligibilityService : IUpdateEligibilityService
    {
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IWorkstationGroupRepository _groupRepository;
        private readonly IUpdateReleaseRepository _releaseRepository;
        private readonly IUpdateTargetRepository _targetRepository;

        public UpdateEligibilityService(
            IRepository<Workstation> workstationRepository,
            IWorkstationGroupRepository groupRepository,
            IUpdateReleaseRepository releaseRepository,
            IUpdateTargetRepository targetRepository)
        {
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _groupRepository = groupRepository ?? throw new ArgumentNullException(nameof(groupRepository));
            _releaseRepository = releaseRepository ?? throw new ArgumentNullException(nameof(releaseRepository));
            _targetRepository = targetRepository ?? throw new ArgumentNullException(nameof(targetRepository));
        }

        public int CalculateRolloutBucket(string pcId, Guid releaseId)
        {
            var normalizedPcId = (pcId ?? string.Empty).Trim().ToUpperInvariant();
            var normalizedReleaseId = releaseId.ToString().ToLowerInvariant();

            string inputKey = $"{normalizedPcId}:{normalizedReleaseId}";
            byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(inputKey));

            uint rawValue = BitConverter.ToUInt32(hashBytes, 0);
            return (int)(rawValue % 100);
        }

        public async Task<UpdateEligibilityResult> EvaluateWorkstationEligibilityAsync(
            Guid workstationId,
            string? reportedVersion = null,
            string? osVersion = null,
            string? architecture = null,
            CancellationToken cancellationToken = default)
        {
            var workstation = await _workstationRepository.GetByIdAsync(workstationId, track: false, cancellationToken);
            if (workstation == null)
            {
                return UpdateEligibilityResult.Ineligible(
                    EligibilityReasonCodes.WorkstationNotFound,
                    $"Workstation '{workstationId}' was not found.");
            }

            if (workstation.IsDeactivated || workstation.IsDisabled)
            {
                return UpdateEligibilityResult.Ineligible(
                    EligibilityReasonCodes.WorkstationDeactivated,
                    $"Workstation '{workstation.PcId}' is deactivated or disabled.");
            }

            if (!workstation.OrganizationEntityId.HasValue || workstation.OrganizationEntityId.Value == Guid.Empty)
            {
                return UpdateEligibilityResult.Ineligible(
                    EligibilityReasonCodes.OrganizationMismatch,
                    $"Workstation '{workstation.PcId}' is not assigned to an organization.");
            }

            var groupIds = await _groupRepository.GetWorkstationGroupIdsForWorkstationAsync(workstation.Id, cancellationToken);
            string versionToUse = !string.IsNullOrWhiteSpace(reportedVersion) ? reportedVersion.Trim() : workstation.ClientVersion;

            var context = new ClientEvaluationContext
            {
                OrganizationId = workstation.OrganizationEntityId.Value,
                WorkstationId = workstation.Id,
                PcId = workstation.PcId,
                SiteId = workstation.SiteEntityId,
                GroupIds = groupIds != null ? groupIds.ToList() : new List<Guid>(),
                CurrentVersion = versionToUse,
                OsVersion = !string.IsNullOrWhiteSpace(osVersion) ? osVersion.Trim() : workstation.OsVersion,
                Architecture = architecture?.Trim()
            };

            return await EvaluateEligibilityAsync(context, cancellationToken);
        }

        public async Task<UpdateEligibilityResult> EvaluateEligibilityAsync(
            ClientEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                return UpdateEligibilityResult.Ineligible(
                    EligibilityReasonCodes.NoUpdateAvailable,
                    "Client evaluation context is null.");
            }

            if (context.OrganizationId == Guid.Empty)
            {
                return UpdateEligibilityResult.Ineligible(
                    EligibilityReasonCodes.OrganizationMismatch,
                    "Organization ID is required in client evaluation context.");
            }

            if (!ClientVersionComparer.TryParseVersion(context.CurrentVersion, out _, out _))
            {
                return UpdateEligibilityResult.Ineligible(
                    EligibilityReasonCodes.ClientVersionIncompatible,
                    $"Current client version '{context.CurrentVersion}' is invalid or malformed.");
            }

            // Query explicit applicable targets for client context
            var applicableTargets = await _targetRepository.GetApplicableTargetsAsync(
                context.OrganizationId,
                context.SiteId,
                context.GroupIds,
                context.WorkstationId,
                track: false,
                cancellationToken);

            // Query active release for fallback if no explicit targets exist
            var activeRelease = await _releaseRepository.GetActiveReleaseAsync(context.OrganizationId, track: false, cancellationToken);

            var candidateEvaluations = new List<CandidateEvaluation>();

            if (applicableTargets.Any())
            {
                foreach (var target in applicableTargets)
                {
                    if (target.Release == null)
                    {
                        var loadedRelease = await _releaseRepository.GetByIdAsync(target.ReleaseId, track: false, cancellationToken);
                        if (loadedRelease != null)
                        {
                            target.Release = loadedRelease;
                        }
                    }

                    if (target.Release != null)
                    {
                        var eval = EvaluateReleaseCandidate(context, target.Release, target);
                        candidateEvaluations.Add(eval);
                    }
                }
            }
            else if (activeRelease != null)
            {
                var eval = EvaluateReleaseCandidate(context, activeRelease, target: null);
                candidateEvaluations.Add(eval);
            }

            if (!candidateEvaluations.Any())
            {
                return UpdateEligibilityResult.Ineligible(
                    EligibilityReasonCodes.NoUpdateAvailable,
                    "No published or active update releases are configured for this organization.");
            }

            // Filter strictly eligible candidates
            var eligibleCandidates = candidateEvaluations.Where(e => e.IsEligible).ToList();

            if (eligibleCandidates.Any())
            {
                // Select highest precedence eligible release candidate
                var bestCandidate = eligibleCandidates
                    .OrderByDescending(c => c.ScopeRank)
                    .ThenByDescending(c => c.VersionComparable, new SemVerComparer())
                    .ThenByDescending(c => c.IsMandatory ? 1 : 0)
                    .ThenByDescending(c => c.Release.CreatedAt)
                    .First();

                var releaseContract = ClientUpdateContractAdapter.ToReleaseContract(bestCandidate.Release);
                var packageContract = ClientUpdateContractAdapter.ToPackageMetadataContract(bestCandidate.Package);

                return UpdateEligibilityResult.Eligible(
                    releaseContract,
                    packageContract,
                    bestCandidate.RolloutBucket,
                    bestCandidate.RolloutPercentage,
                    bestCandidate.IsMandatory,
                    bestCandidate.MinimumSupportedVersion,
                    bestCandidate.ReasonDetails);
            }

            // Return most descriptive ineligible diagnostic reason
            var primaryFailure = candidateEvaluations
                .OrderByDescending(c => GetReasonCodePriority(c.ReasonCode))
                .First();

            return UpdateEligibilityResult.Ineligible(
                primaryFailure.ReasonCode,
                primaryFailure.ReasonDetails,
                primaryFailure.RolloutBucket,
                primaryFailure.Release?.Version);
        }

        private CandidateEvaluation EvaluateReleaseCandidate(
            ClientEvaluationContext context,
            UpdateRelease release,
            UpdateTarget? target)
        {
            var eval = new CandidateEvaluation
            {
                Release = release,
                Target = target,
                RolloutBucket = CalculateRolloutBucket(context.PcId, release.Id),
                RolloutPercentage = target?.RolloutPercentage ?? 100,
                MinimumSupportedVersion = target?.MinimumSupportedVersion,
                ScopeRank = GetScopeRank(target?.TargetType)
            };

            // 1. Organization Isolation
            if (release.OrganizationId != context.OrganizationId)
            {
                eval.SetIneligible(EligibilityReasonCodes.OrganizationMismatch, $"Release organization '{release.OrganizationId}' does not match client organization '{context.OrganizationId}'.");
                return eval;
            }

            // 2. Release Lifecycle State
            if (release.Status == UpdateReleaseStatus.Revoked)
            {
                eval.SetIneligible(EligibilityReasonCodes.ReleaseRevoked, $"Release '{release.Version}' has been revoked.");
                return eval;
            }

            if (release.Status != UpdateReleaseStatus.Published && release.Status != UpdateReleaseStatus.Active)
            {
                eval.SetIneligible(EligibilityReasonCodes.ReleaseNotActive, $"Release '{release.Version}' is in '{release.Status}' state and cannot be distributed.");
                return eval;
            }

            // 3. Package Verification & Cryptographic Readiness
            var package = release.Packages.FirstOrDefault();
            if (package == null)
            {
                eval.SetIneligible(EligibilityReasonCodes.PackageNotReady, $"Release '{release.Version}' has no package attached.");
                return eval;
            }

            eval.Package = package;

            if (package.LifecycleState != UpdatePackageLifecycleState.Signed && package.LifecycleState != UpdatePackageLifecycleState.Ready)
            {
                eval.SetIneligible(EligibilityReasonCodes.PackageNotReady, $"Package '{package.FileName}' is in '{package.LifecycleState}' state and is not ready.");
                return eval;
            }

            if (string.IsNullOrWhiteSpace(package.SHA256) || string.IsNullOrWhiteSpace(package.Signature))
            {
                eval.SetIneligible(EligibilityReasonCodes.SignatureInvalid, $"Package '{package.FileName}' is missing SHA-256 checksum or RSA signature.");
                return eval;
            }

            // 4. Version Rules (ClientVersionComparer)
            string currentVer = context.CurrentVersion;
            string targetVer = release.Version;

            eval.VersionComparable = targetVer;

            int cmp = ClientVersionComparer.Compare(targetVer, currentVer);

            if (cmp == 0)
            {
                eval.SetIneligible(EligibilityReasonCodes.ClientAlreadyCurrent, $"Client is already on version '{currentVer}'.");
                return eval;
            }

            bool isDowngrade = ClientVersionComparer.IsDowngrade(currentVer, targetVer);
            if (isDowngrade)
            {
                // Check if explicitly authorized rollback or emergency release
                bool isAuthorizedRollback = release.ReleaseType == UpdateReleaseType.Emergency ||
                                           (release.Metadata != null && release.Metadata.Contains("Rollback", StringComparison.OrdinalIgnoreCase));

                if (!isAuthorizedRollback)
                {
                    eval.SetIneligible(EligibilityReasonCodes.RollbackNotAllowed, $"Target release '{targetVer}' is older than current client version '{currentVer}' and rollback is not authorized.");
                    return eval;
                }
            }

            // 5. Minimum Supported Version Policy
            string? minVer = target?.MinimumSupportedVersion;
            bool isBelowMinimum = ClientVersionComparer.IsBelowMinimumVersion(currentVer, minVer);

            eval.IsMandatory = release.ReleaseType is UpdateReleaseType.Security or UpdateReleaseType.Emergency ||
                              (target != null && target.IsMandatoryOverride) ||
                              isBelowMinimum;

            if (isBelowMinimum && cmp < 0)
            {
                eval.SetIneligible(EligibilityReasonCodes.MinimumVersionEnforced, $"Current client version '{currentVer}' is below minimum supported version '{minVer}', but release version '{targetVer}' is also below current version.");
                return eval;
            }

            // 6. Staged Rollout Evaluation
            if (!eval.IsMandatory)
            {
                if (eval.RolloutBucket >= eval.RolloutPercentage)
                {
                    eval.SetIneligible(EligibilityReasonCodes.RolloutNotEligible, $"Client bucket '{eval.RolloutBucket}' exceeds rollout percentage '{eval.RolloutPercentage}%'.");
                    return eval;
                }
            }

            eval.IsEligible = true;
            eval.ReasonCode = EligibilityReasonCodes.Eligible;
            eval.ReasonDetails = eval.IsMandatory
                ? $"Mandatory update release '{release.Version}' is available."
                : $"Client bucket '{eval.RolloutBucket}' is eligible for release '{release.Version}' (rollout: {eval.RolloutPercentage}%).";

            return eval;
        }

        private static int GetScopeRank(ConfigurationTargetType? scope)
        {
            return scope switch
            {
                ConfigurationTargetType.Workstation => 4,
                ConfigurationTargetType.Group => 3,
                ConfigurationTargetType.Site => 2,
                ConfigurationTargetType.Global => 1,
                _ => 0
            };
        }

        private static int GetReasonCodePriority(string reasonCode)
        {
            return reasonCode switch
            {
                EligibilityReasonCodes.ClientAlreadyCurrent => 10,
                EligibilityReasonCodes.RolloutNotEligible => 9,
                EligibilityReasonCodes.RollbackNotAllowed => 8,
                EligibilityReasonCodes.MinimumVersionEnforced => 7,
                EligibilityReasonCodes.PackageNotReady => 6,
                EligibilityReasonCodes.SignatureInvalid => 5,
                EligibilityReasonCodes.ReleaseRevoked => 4,
                EligibilityReasonCodes.ReleaseNotActive => 3,
                EligibilityReasonCodes.NoTargetMatch => 2,
                _ => 1
            };
        }

        private class CandidateEvaluation
        {
            public UpdateRelease Release { get; set; } = null!;
            public UpdatePackage Package { get; set; } = null!;
            public UpdateTarget? Target { get; set; }
            public bool IsEligible { get; set; }
            public string ReasonCode { get; set; } = EligibilityReasonCodes.NoUpdateAvailable;
            public string ReasonDetails { get; set; } = string.Empty;
            public int RolloutBucket { get; set; }
            public int RolloutPercentage { get; set; } = 100;
            public bool IsMandatory { get; set; }
            public string? MinimumSupportedVersion { get; set; }
            public int ScopeRank { get; set; }
            public string VersionComparable { get; set; } = string.Empty;

            public void SetIneligible(string code, string details)
            {
                IsEligible = false;
                ReasonCode = code;
                ReasonDetails = details;
            }
        }

        private class SemVerComparer : IComparer<string>
        {
            public int Compare(string? x, string? y)
            {
                if (x == null && y == null) return 0;
                if (x == null) return -1;
                if (y == null) return 1;

                return ClientVersionComparer.Compare(x, y);
            }
        }
    }
}

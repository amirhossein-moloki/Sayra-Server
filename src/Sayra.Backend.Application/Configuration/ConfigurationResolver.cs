using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Configuration.Models;
using Sayra.Backend.Domain;
using Sayra.Backend.Shared;

#nullable enable

namespace Sayra.Backend.Application.Configuration
{
    public class ConfigurationResolver : IConfigurationResolver
    {
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IRepository<Organization> _organizationRepository;
        private readonly IRepository<Site> _siteRepository;
        private readonly IWorkstationGroupRepository _groupRepository;
        private readonly IConfigurationAssignmentRepository _assignmentRepository;
        private readonly IConfigurationTargetRepository _targetRepository;
        private readonly IConfigurationPackageRepository _packageRepository;
        private readonly IConfigurationPublicationRepository? _publicationRepository;
        private readonly IConfigurationCache? _configurationCache;
        private readonly IConfigurationDeltaEngine _deltaEngine;
        private readonly IConfigurationNormalizer _normalizer;
        private readonly IConfigurationValidator _validator;

        public ConfigurationResolver(
            IRepository<Workstation> workstationRepository,
            IRepository<Organization> organizationRepository,
            IRepository<Site> siteRepository,
            IWorkstationGroupRepository groupRepository,
            IConfigurationAssignmentRepository assignmentRepository,
            IConfigurationTargetRepository targetRepository,
            IConfigurationPackageRepository packageRepository,
            IConfigurationDeltaEngine deltaEngine,
            IConfigurationNormalizer normalizer,
            IConfigurationValidator validator,
            IConfigurationPublicationRepository? publicationRepository = null,
            IConfigurationCache? configurationCache = null)
        {
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _organizationRepository = organizationRepository ?? throw new ArgumentNullException(nameof(organizationRepository));
            _siteRepository = siteRepository ?? throw new ArgumentNullException(nameof(siteRepository));
            _groupRepository = groupRepository ?? throw new ArgumentNullException(nameof(groupRepository));
            _assignmentRepository = assignmentRepository ?? throw new ArgumentNullException(nameof(assignmentRepository));
            _targetRepository = targetRepository ?? throw new ArgumentNullException(nameof(targetRepository));
            _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
            _deltaEngine = deltaEngine ?? throw new ArgumentNullException(nameof(deltaEngine));
            _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _publicationRepository = publicationRepository;
            _configurationCache = configurationCache;
        }

        public async Task<Result<ConfigurationResolutionResult>> ResolveEffectiveConfigurationAsync(
            Guid workstationId,
            CancellationToken cancellationToken = default)
        {
            // 1. Authoritative Identity & Context Validation
            var workstation = await _workstationRepository.GetByIdAsync(workstationId, track: false, cancellationToken);
            if (workstation == null)
            {
                return Result<ConfigurationResolutionResult>.Failure("WORKSTATION_NOT_FOUND", $"Workstation '{workstationId}' not found.");
            }

            if (workstation.IsDeactivated)
            {
                return Result<ConfigurationResolutionResult>.Failure("WORKSTATION_DEACTIVATED", $"Workstation '{workstation.PcId}' is deactivated.");
            }

            if (!workstation.OrganizationEntityId.HasValue)
            {
                return Result<ConfigurationResolutionResult>.Failure("WORKSTATION_NOT_ASSIGNED_TO_ORGANIZATION", $"Workstation '{workstation.PcId}' is not assigned to an organization.");
            }

            var orgId = workstation.OrganizationEntityId.Value;
            var org = await _organizationRepository.GetByIdAsync(orgId, track: false, cancellationToken);
            if (org == null || !org.CanOperate())
            {
                return Result<ConfigurationResolutionResult>.Failure("ORGANIZATION_INACTIVE", $"Organization for workstation '{workstation.PcId}' is inactive or not found.");
            }

            // Site Validation
            Guid? siteId = workstation.SiteEntityId;
            if (siteId.HasValue && siteId.Value != Guid.Empty)
            {
                var site = await _siteRepository.GetByIdAsync(siteId.Value, track: false, cancellationToken);
                if (site == null)
                {
                    return Result<ConfigurationResolutionResult>.Failure("SITE_NOT_FOUND", $"Site '{siteId.Value}' not found.");
                }
                if (site.OrganizationId != orgId)
                {
                    return Result<ConfigurationResolutionResult>.Failure("CROSS_ORGANIZATION_TARGET_REJECTED", $"Site '{site.Name}' belongs to organization '{site.OrganizationId}', not '{orgId}'.");
                }
                if (!site.CanOperate())
                {
                    return Result<ConfigurationResolutionResult>.Failure("SITE_INACTIVE", $"Site '{site.Name}' is inactive.");
                }
            }

            // Group Memberships Validation & Active Filter
            var rawGroupIds = await _groupRepository.GetWorkstationGroupIdsForWorkstationAsync(workstation.Id, cancellationToken) ?? new List<Guid>();
            var activeGroups = new List<WorkstationGroup>();
            foreach (var gid in rawGroupIds)
            {
                var g = await _groupRepository.GetByIdAsync(gid, track: false, cancellationToken);
                if (g != null && g.CanOperate() && g.OrganizationId == orgId)
                {
                    activeGroups.Add(g);
                }
            }

            var activeGroupIds = activeGroups.Select(g => g.Id).ToList();

            // Cache Lookup
            if (_configurationCache != null)
            {
                var cachedConfig = await _configurationCache.GetEffectiveConfigurationAsync(
                    orgId, workstation.Id, siteId, activeGroupIds, cancellationToken);

                if (cachedConfig != null)
                {
                    var cachedResolution = new ConfigurationResolutionResult
                    {
                        EffectiveConfigurationJson = cachedConfig.EffectiveConfigurationJson,
                        SchemaVersion = cachedConfig.SchemaVersion,
                        AppliedSources = cachedConfig.AppliedSources ?? new List<AppliedConfigurationSourceDto>(),
                        FieldTraces = cachedConfig.FieldTraces ?? new List<ConfigurationFieldTraceDto>(),
                        Warnings = cachedConfig.Warnings ?? new List<string>()
                    };

                    return Result<ConfigurationResolutionResult>.Success(cachedResolution);
                }
            }

            // Stampede Lock (optional lock protection during miss)
            IDisposable? stampedeLock = null;
            if (_configurationCache != null)
            {
                stampedeLock = await _configurationCache.AcquireStampedeLockAsync(orgId, workstation.Id, cancellationToken);
            }

            try
            {
                // Double-check cache if lock was acquired
                if (_configurationCache != null && stampedeLock != null)
                {
                    var secondCached = await _configurationCache.GetEffectiveConfigurationAsync(
                        orgId, workstation.Id, siteId, activeGroupIds, cancellationToken);

                    if (secondCached != null)
                    {
                        var cachedResolution = new ConfigurationResolutionResult
                        {
                            EffectiveConfigurationJson = secondCached.EffectiveConfigurationJson,
                            SchemaVersion = secondCached.SchemaVersion,
                            AppliedSources = secondCached.AppliedSources ?? new List<AppliedConfigurationSourceDto>(),
                            FieldTraces = secondCached.FieldTraces ?? new List<ConfigurationFieldTraceDto>(),
                            Warnings = secondCached.Warnings ?? new List<string>()
                        };

                        return Result<ConfigurationResolutionResult>.Success(cachedResolution);
                    }
                }

                // 2. Retrieve Applicable Assignments
                var assignments = await _assignmentRepository.GetApplicableAssignmentsAsync(orgId, siteId, activeGroupIds, workstation.Id, cancellationToken) ?? new List<ConfigurationAssignment>();
                assignments = assignments.Where(a => a.IsActive).ToList();

                if (!assignments.Any())
                {
                    // Return default empty configuration normalized and validated
                    var defaultNormalized = _normalizer.NormalizeToJson("{}");
                    var defaultValidation = _validator.Validate(defaultNormalized);
                    if (!defaultValidation.IsValid)
                    {
                        return Result<ConfigurationResolutionResult>.Failure("EffectiveConfigurationInvalid", "Default empty configuration is invalid.");
                    }

                    var emptyResult = new ConfigurationResolutionResult
                    {
                        EffectiveConfigurationJson = defaultNormalized,
                        SchemaVersion = "1.0",
                        AppliedSources = new List<AppliedConfigurationSourceDto>(),
                        FieldTraces = new List<ConfigurationFieldTraceDto>(),
                        Warnings = new List<string> { "No applicable configuration assignments found. Returning default normalized configuration." }
                    };

                    if (_configurationCache != null)
                    {
                        await _configurationCache.SetEffectiveConfigurationAsync(
                            orgId, workstation.Id, siteId, activeGroupIds,
                            new CachedEffectiveConfiguration
                            {
                                SchemaVersion = emptyResult.SchemaVersion,
                                EffectiveConfigurationJson = emptyResult.EffectiveConfigurationJson,
                                AppliedSources = emptyResult.AppliedSources,
                                FieldTraces = emptyResult.FieldTraces,
                                Warnings = emptyResult.Warnings
                            },
                            cancellationToken);
                    }

                    return Result<ConfigurationResolutionResult>.Success(emptyResult);
                }

                // 3. Load Targets, Packages, and Reconstruct Full Payloads
                var eligibleCandidates = new List<EligibleCandidate>();

                foreach (var assignment in assignments)
                {
                    var target = await _targetRepository.GetByIdAsync(assignment.ConfigurationTargetId, track: false, cancellationToken);
                    if (target == null || target.OrganizationId != orgId)
                    {
                        continue;
                    }

                    var package = await _packageRepository.GetByIdAsync(assignment.ConfigurationPackageId, track: false, cancellationToken);
                    if (package == null || !package.IsActive)
                    {
                        // Filter out inactive/revoked packages
                        continue;
                    }

                    // If Publication tracking is present, enforce Active lifecycle state requirement
                    if (_publicationRepository != null)
                    {
                        var activePub = await _publicationRepository.GetActivePublicationForTargetAsync(target.Id, cancellationToken);
                        if (activePub == null || activePub.ConfigurationPackageId != package.Id)
                        {
                            // Filter out unpublished, non-active, superseded, or revoked packages
                            continue;
                        }
                    }

                    WorkstationGroup? associatedGroup = null;
                    if (target.TargetType == ConfigurationTargetType.Group && target.GroupId.HasValue)
                    {
                        associatedGroup = activeGroups.FirstOrDefault(g => g.Id == target.GroupId.Value);
                        if (associatedGroup == null)
                        {
                            // Group is inactive or invalid
                            continue;
                        }
                    }

                    string normalizedPayload;
                    try
                    {
                        normalizedPayload = await ReconstructPackageContentAsync(package, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        return Result<ConfigurationResolutionResult>.Failure("ConfigurationVersionUnavailable", $"Failed to reconstruct configuration package v{package.VersionNumber}: {ex.Message}");
                    }

                    // Ensure candidate payload is valid JSON
                    try
                    {
                        using var _ = JsonDocument.Parse(normalizedPayload);
                    }
                    catch (Exception ex)
                    {
                        return Result<ConfigurationResolutionResult>.Failure("TargetConfigurationInvalid", $"Candidate package v{package.VersionNumber} for target '{target.TargetType}' contains invalid JSON: {ex.Message}");
                    }

                    eligibleCandidates.Add(new EligibleCandidate
                    {
                        Assignment = assignment,
                        Target = target,
                        Package = package,
                        Group = associatedGroup,
                        ReconstructedNormalizedJson = normalizedPayload
                    });
                }

                if (!eligibleCandidates.Any())
                {
                    var defaultNormalized = _normalizer.NormalizeToJson("{}");
                    var emptyResult = new ConfigurationResolutionResult
                    {
                        EffectiveConfigurationJson = defaultNormalized,
                        SchemaVersion = "1.0",
                        AppliedSources = new List<AppliedConfigurationSourceDto>(),
                        FieldTraces = new List<ConfigurationFieldTraceDto>(),
                        Warnings = new List<string> { "No active eligible configuration packages found. Returning default configuration." }
                    };

                    if (_configurationCache != null)
                    {
                        await _configurationCache.SetEffectiveConfigurationAsync(
                            orgId, workstation.Id, siteId, activeGroupIds,
                            new CachedEffectiveConfiguration
                            {
                                SchemaVersion = emptyResult.SchemaVersion,
                                EffectiveConfigurationJson = emptyResult.EffectiveConfigurationJson,
                                AppliedSources = emptyResult.AppliedSources,
                                FieldTraces = emptyResult.FieldTraces,
                                Warnings = emptyResult.Warnings
                            },
                            cancellationToken);
                    }

                    return Result<ConfigurationResolutionResult>.Success(emptyResult);
                }

                // 4. Precedence Hierarchy & Conflict Resolution
                var orderedLayers = selectOrderedCandidateLayers(eligibleCandidates);

                // 5. JSON Field-Level Merging
                var rootResultObject = new JsonObject();
                var fieldTraces = new Dictionary<string, ConfigurationFieldTraceDto>(StringComparer.Ordinal);
                var appliedSources = new List<AppliedConfigurationSourceDto>();

                foreach (var candidate in orderedLayers)
                {
                    appliedSources.Add(new AppliedConfigurationSourceDto
                    {
                        TargetType = candidate.Target.TargetType.ToString(),
                        TargetId = candidate.Target.Id,
                        PackageId = candidate.Package.Id,
                        PackageName = candidate.Package.Name,
                        VersionNumber = candidate.Package.VersionNumber,
                        Version = candidate.Package.Version,
                        AssignmentId = candidate.Assignment.Id
                    });

                    JsonNode? candidateNode;
                    try
                    {
                        candidateNode = JsonNode.Parse(candidate.ReconstructedNormalizedJson);
                    }
                    catch (Exception ex)
                    {
                        return Result<ConfigurationResolutionResult>.Failure("ConfigurationMergeFailed", $"Failed to parse JSON for candidate v{candidate.Package.VersionNumber}: {ex.Message}");
                    }

                    if (candidateNode is JsonObject candidateObj)
                    {
                        MergeObjects(rootResultObject, candidateObj, string.Empty, candidate, fieldTraces);
                    }
                }

                // 6. Post-Merge Pipeline: Normalization & Validation
                string mergedRawJson = rootResultObject.ToJsonString();
                string mergedNormalizedJson;
                try
                {
                    mergedNormalizedJson = _normalizer.NormalizeToJson(mergedRawJson);
                }
                catch (Exception ex)
                {
                    return Result<ConfigurationResolutionResult>.Failure("ConfigurationMergeFailed", $"Failed to normalize merged effective configuration: {ex.Message}");
                }

                var finalValidation = _validator.Validate(mergedNormalizedJson);
                if (!finalValidation.IsValid)
                {
                    var errors = string.Join("; ", finalValidation.Errors.Select(e => $"[{e.Path}] {e.Code}: {e.Message}"));
                    return Result<ConfigurationResolutionResult>.Failure("EffectiveConfigurationInvalid", $"Effective configuration post-merge validation failed: {errors}");
                }

                var result = new ConfigurationResolutionResult
                {
                    EffectiveConfigurationJson = mergedNormalizedJson,
                    SchemaVersion = eligibleCandidates.Select(c => c.Package.SchemaVersion).FirstOrDefault() ?? "1.0",
                    AppliedSources = appliedSources,
                    FieldTraces = fieldTraces.Values.OrderBy(f => f.Path, StringComparer.Ordinal).ToList(),
                    Warnings = new List<string>()
                };

                if (_configurationCache != null)
                {
                    await _configurationCache.SetEffectiveConfigurationAsync(
                        orgId, workstation.Id, siteId, activeGroupIds,
                        new CachedEffectiveConfiguration
                        {
                            SchemaVersion = result.SchemaVersion,
                            EffectiveConfigurationJson = result.EffectiveConfigurationJson,
                            AppliedSources = result.AppliedSources,
                            FieldTraces = result.FieldTraces,
                            Warnings = result.Warnings
                        },
                        cancellationToken);
                }

                return Result<ConfigurationResolutionResult>.Success(result);
            }
            finally
            {
                stampedeLock?.Dispose();
            }
        }

        private async Task<string> ReconstructPackageContentAsync(ConfigurationPackage package, CancellationToken cancellationToken)
        {
            if (package.PayloadType == ConfigurationPayloadType.Full)
            {
                return package.Content;
            }

            var packages = await _packageRepository.GetVersionRangeAsync(package.Name, 1, package.VersionNumber, cancellationToken);
            if (packages == null || !packages.Any())
            {
                throw new InvalidOperationException($"Version history for '{package.Name}' not found.");
            }

            var map = packages.ToDictionary(p => p.VersionNumber);
            var current = package;
            var chain = new List<ConfigurationPackage> { current };

            while (current.PayloadType == ConfigurationPayloadType.Delta)
            {
                if (!current.BaseVersionNumber.HasValue)
                {
                    throw new InvalidOperationException($"Delta package v{current.VersionNumber} is missing BaseVersionNumber.");
                }

                if (!map.TryGetValue(current.BaseVersionNumber.Value, out var basePkg))
                {
                    throw new InvalidOperationException($"Base version v{current.BaseVersionNumber.Value} not found in version history.");
                }

                chain.Add(basePkg);
                current = basePkg;
            }

            chain.Reverse();

            var rootFull = chain[0];
            string content = rootFull.Content;

            for (int i = 1; i < chain.Count; i++)
            {
                var deltaPkg = chain[i];
                content = _deltaEngine.ApplyDelta(content, deltaPkg.Content);
            }

            return content;
        }

        private List<EligibleCandidate> selectOrderedCandidateLayers(List<EligibleCandidate> candidates)
        {
            var result = new List<EligibleCandidate>();

            // 1. Global layer
            var globalCandidate = candidates
                .Where(c => c.Target.TargetType == ConfigurationTargetType.Global)
                .OrderByDescending(c => c.Package.VersionNumber)
                .ThenByDescending(c => c.Assignment.CreatedAt)
                .ThenBy(c => c.Assignment.Id)
                .FirstOrDefault();

            if (globalCandidate != null)
            {
                result.Add(globalCandidate);
            }

            // 2. Site layer
            var siteCandidate = candidates
                .Where(c => c.Target.TargetType == ConfigurationTargetType.Site)
                .OrderByDescending(c => c.Package.VersionNumber)
                .ThenByDescending(c => c.Assignment.CreatedAt)
                .ThenBy(c => c.Assignment.Id)
                .FirstOrDefault();

            if (siteCandidate != null)
            {
                result.Add(siteCandidate);
            }

            // 3. Group layer(s) - Deterministically ordered by Group.Code asc, then Group.Id asc
            var groupCandidatesByGroup = candidates
                .Where(c => c.Target.TargetType == ConfigurationTargetType.Group && c.Group != null)
                .GroupBy(c => c.Group!.Id);

            var winningGroupCandidates = new List<EligibleCandidate>();
            foreach (var grpGroup in groupCandidatesByGroup)
            {
                var winningForGroup = grpGroup
                    .OrderByDescending(c => c.Package.VersionNumber)
                    .ThenByDescending(c => c.Assignment.CreatedAt)
                    .ThenBy(c => c.Assignment.Id)
                    .First();

                winningGroupCandidates.Add(winningForGroup);
            }

            winningGroupCandidates = winningGroupCandidates
                .OrderBy(c => c.Group!.Code, StringComparer.Ordinal)
                .ThenBy(c => c.Group!.Id)
                .ToList();

            result.AddRange(winningGroupCandidates);

            // 4. Workstation layer
            var wsCandidate = candidates
                .Where(c => c.Target.TargetType == ConfigurationTargetType.Workstation)
                .OrderByDescending(c => c.Package.VersionNumber)
                .ThenByDescending(c => c.Assignment.CreatedAt)
                .ThenBy(c => c.Assignment.Id)
                .FirstOrDefault();

            if (wsCandidate != null)
            {
                result.Add(wsCandidate);
            }

            return result;
        }

        private void MergeObjects(
            JsonObject targetObj,
            JsonObject sourceObj,
            string currentPath,
            EligibleCandidate sourceCandidate,
            Dictionary<string, ConfigurationFieldTraceDto> fieldTraces)
        {
            foreach (var kvp in sourceObj)
            {
                string propName = kvp.Key;
                string path = string.IsNullOrEmpty(currentPath) ? $"/{propName}" : $"{currentPath}/{propName}";
                JsonNode? sourceNode = kvp.Value;

                if (sourceNode is JsonObject sourceSubObj)
                {
                    if (targetObj.ContainsKey(propName) && targetObj[propName] is JsonObject targetSubObj)
                    {
                        // Both are objects -> deep merge
                        MergeObjects(targetSubObj, sourceSubObj, path, sourceCandidate, fieldTraces);
                    }
                    else
                    {
                        // Target does not have this object or target is not an object -> copy object
                        var newTargetObj = new JsonObject();
                        targetObj[propName] = newTargetObj;
                        MergeObjects(newTargetObj, sourceSubObj, path, sourceCandidate, fieldTraces);
                    }
                }
                else if (sourceNode is JsonArray sourceArray)
                {
                    // Array replacement semantics
                    targetObj[propName] = JsonNode.Parse(sourceArray.ToJsonString());
                    fieldTraces[path] = new ConfigurationFieldTraceDto
                    {
                        Path = path,
                        TargetType = sourceCandidate.Target.TargetType.ToString(),
                        TargetId = sourceCandidate.Target.Id,
                        VersionNumber = sourceCandidate.Package.VersionNumber
                    };
                }
                else
                {
                    // Scalar or explicit null -> replace
                    targetObj[propName] = sourceNode == null ? null : JsonNode.Parse(sourceNode.ToJsonString());
                    fieldTraces[path] = new ConfigurationFieldTraceDto
                    {
                        Path = path,
                        TargetType = sourceCandidate.Target.TargetType.ToString(),
                        TargetId = sourceCandidate.Target.Id,
                        VersionNumber = sourceCandidate.Package.VersionNumber
                    };
                }
            }
        }

        private class EligibleCandidate
        {
            public ConfigurationAssignment Assignment { get; set; } = null!;
            public ConfigurationTarget Target { get; set; } = null!;
            public ConfigurationPackage Package { get; set; } = null!;
            public WorkstationGroup? Group { get; set; }
            public string ReconstructedNormalizedJson { get; set; } = "{}";
        }
    }
}

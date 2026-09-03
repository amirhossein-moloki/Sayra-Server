using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Configuration.Models;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Shared;

#nullable enable

namespace Sayra.Backend.Application.Configuration
{
    public enum ConfigurationSyncStatus
    {
        UpToDate,
        FullPackage,
        DeltaPackage
    }

    public class ConfigurationSyncResult
    {
        public ConfigurationSyncStatus Status { get; set; }
        public long VersionNumber { get; set; }
        public long? BaseVersionNumber { get; set; }
        public string Version { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string IssuedBy { get; set; } = "system";
        public string Hash { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public string? KeyId { get; set; }
        public string PayloadType { get; set; } = "Full"; // "Full" or "Delta"
        public object? Payload { get; set; }
        public string? TargetClient { get; set; }
        public string? TargetGroup { get; set; }
    }

    public record SynchronizeConfigurationQuery(
        string? ClientPcId,
        long? ClientVersion,
        Guid? WorkstationId = null,
        Guid? OrganizationId = null) : IQuery<ConfigurationSyncResult>;

    public class SynchronizeConfigurationQueryHandler : IQueryHandler<SynchronizeConfigurationQuery, ConfigurationSyncResult>
    {
        private readonly IRepository<Workstation> _workstationRepository;
        private readonly IConfigurationResolver _resolver;
        private readonly IConfigurationSigningService _signingService;
        private readonly IConfigurationHashService _hashService;
        private readonly ICanonicalConfigurationSerializer _canonicalSerializer;
        private readonly IConfigurationPackageRepository _packageRepository;
        private readonly IConfigurationDeltaEngine _deltaEngine;
        private readonly ILogger<SynchronizeConfigurationQueryHandler> _logger;

        public SynchronizeConfigurationQueryHandler(
            IRepository<Workstation> workstationRepository,
            IConfigurationResolver resolver,
            IConfigurationSigningService signingService,
            IConfigurationHashService hashService,
            ICanonicalConfigurationSerializer canonicalSerializer,
            IConfigurationPackageRepository packageRepository,
            IConfigurationDeltaEngine deltaEngine,
            ILogger<SynchronizeConfigurationQueryHandler> logger)
        {
            _workstationRepository = workstationRepository ?? throw new ArgumentNullException(nameof(workstationRepository));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _signingService = signingService ?? throw new ArgumentNullException(nameof(signingService));
            _hashService = hashService ?? throw new ArgumentNullException(nameof(hashService));
            _canonicalSerializer = canonicalSerializer ?? throw new ArgumentNullException(nameof(canonicalSerializer));
            _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
            _deltaEngine = deltaEngine ?? throw new ArgumentNullException(nameof(deltaEngine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<ConfigurationSyncResult>> HandleAsync(SynchronizeConfigurationQuery query, CancellationToken cancellationToken = default)
        {
            if (query == null)
            {
                return Result.Failure<ConfigurationSyncResult>("NULL_QUERY", "Query cannot be null.");
            }

            // 1. Resolve Workstation Identity
            Workstation? workstation = null;
            if (query.WorkstationId.HasValue && query.WorkstationId.Value != Guid.Empty)
            {
                workstation = await _workstationRepository.GetByIdAsync(query.WorkstationId.Value, track: false, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(query.ClientPcId))
            {
                var pcIdUpper = query.ClientPcId.Trim().ToUpperInvariant();
                var workstations = await _workstationRepository.GetAllAsync(track: false, cancellationToken);
                workstation = workstations.FirstOrDefault(w => w.PcId.Equals(pcIdUpper, StringComparison.OrdinalIgnoreCase));
            }

            if (workstation == null)
            {
                _logger.LogWarning("Configuration sync failed: Workstation identity not found for PcId '{PcId}', Id '{WorkstationId}'", query.ClientPcId, query.WorkstationId);
                return Result.Failure<ConfigurationSyncResult>("WORKSTATION_NOT_FOUND", "Workstation entity not found for the authenticated session.");
            }

            if (workstation.IsDisabled || workstation.IsDeactivated)
            {
                _logger.LogWarning("Configuration sync rejected: Workstation '{PcId}' is disabled or deactivated.", workstation.PcId);
                return Result.Failure<ConfigurationSyncResult>("WORKSTATION_DISABLED", $"Workstation '{workstation.PcId}' is disabled or deactivated.");
            }

            // 2. Tenant/Organization Boundary Isolation
            if (query.OrganizationId.HasValue && workstation.OrganizationEntityId.HasValue && query.OrganizationId.Value != workstation.OrganizationEntityId.Value)
            {
                _logger.LogWarning("Configuration sync rejected: Cross-organization access attempt by PcId '{PcId}' in Org '{WorkstationOrg}' for Org '{RequestedOrg}'",
                    workstation.PcId, workstation.OrganizationEntityId, query.OrganizationId);
                return Result.Failure<ConfigurationSyncResult>("CROSS_ORGANIZATION_ACCESS_DENIED", "Access denied: Workstation does not belong to the requested organization.");
            }

            // 3. Resolve Effective Configuration using Stage 06-05 Resolver
            var resolutionResult = await _resolver.ResolveEffectiveConfigurationAsync(workstation.Id, cancellationToken);
            if (!resolutionResult.IsSuccess)
            {
                _logger.LogWarning("Configuration resolution failed for Workstation '{PcId}': {ErrorCode} - {ErrorMessage}",
                    workstation.PcId, resolutionResult.ErrorCode, resolutionResult.ErrorMessage);
                return Result.Failure<ConfigurationSyncResult>(
                    resolutionResult.ErrorCode ?? "CONFIGURATION_RESOLUTION_FAILED",
                    resolutionResult.ErrorMessage ?? "Failed to resolve effective configuration.");
            }

            var resolution = resolutionResult.Value!;
            string effectiveJson = resolution.EffectiveConfigurationJson;

            // Determine authoritative version number from applied packages
            long authoritativeVersionNumber = resolution.AppliedSources.Count > 0
                ? resolution.AppliedSources.Max(s => s.VersionNumber)
                : 1;

            // 4. Digitally Sign / Hash Effective Configuration (Stage 06-06)
            string hashHex;
            string signatureBase64 = string.Empty;
            string? keyId = null;

            try
            {
                var signResult = await _signingService.SignPackageAsync(effectiveJson, cancellationToken: cancellationToken);
                hashHex = signResult.Hash;
                signatureBase64 = signResult.Signature;
                keyId = signResult.KeyId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Digital signing unavailable for Workstation '{PcId}'. Falling back to canonical hash.", workstation.PcId);
                byte[] canonicalBytes = _canonicalSerializer.SerializeToCanonicalBytes(effectiveJson);
                hashHex = _hashService.ComputeHash(canonicalBytes);
            }

            // 5. Version Comparison & Package Selection (UpToDate, Delta, or Full)
            long? clientVersion = query.ClientVersion;

            // Case A: Up To Date -> 304 Not Modified
            if (clientVersion.HasValue && clientVersion.Value == authoritativeVersionNumber)
            {
                _logger.LogInformation("Configuration sync complete for Workstation '{PcId}': Status=UpToDate, Version=v{Version}",
                    workstation.PcId, authoritativeVersionNumber);

                return Result.Success(new ConfigurationSyncResult
                {
                    Status = ConfigurationSyncStatus.UpToDate,
                    VersionNumber = authoritativeVersionNumber,
                    Version = $"v{authoritativeVersionNumber}",
                    Hash = hashHex,
                    Signature = signatureBase64,
                    KeyId = keyId,
                    PayloadType = "Full",
                    Payload = null,
                    TargetClient = workstation.PcId,
                    TargetGroup = resolution.AppliedSources.FirstOrDefault(s => s.TargetType == "Group")?.PackageName
                });
            }

            // Case B: Client Version is smaller -> Attempt Delta Package if safe
            if (clientVersion.HasValue && clientVersion.Value > 0 && clientVersion.Value < authoritativeVersionNumber)
            {
                var deltaResult = await TryBuildDeltaPackageAsync(
                    workstation,
                    clientVersion.Value,
                    authoritativeVersionNumber,
                    effectiveJson,
                    hashHex,
                    signatureBase64,
                    keyId,
                    resolution,
                    cancellationToken);

                if (deltaResult != null)
                {
                    _logger.LogInformation("Configuration sync complete for Workstation '{PcId}': Status=DeltaPackage, BaseVersion=v{BaseVersion}, TargetVersion=v{TargetVersion}",
                        workstation.PcId, clientVersion.Value, authoritativeVersionNumber);

                    return Result.Success(deltaResult);
                }

                _logger.LogInformation("Delta chain unavailable or unsafe for Workstation '{PcId}' (v{ClientVersion} -> v{TargetVersion}). Falling back to Full package.",
                    workstation.PcId, clientVersion.Value, authoritativeVersionNumber);
            }

            // Case C: Missing/Invalid Version, Older/Unknown Client, or Delta Fallback -> Full Package Response
            object? parsedPayload;
            try
            {
                using var doc = JsonDocument.Parse(effectiveJson);
                parsedPayload = doc.RootElement.Clone();
            }
            catch
            {
                parsedPayload = effectiveJson;
            }

            _logger.LogInformation("Configuration sync complete for Workstation '{PcId}': Status=FullPackage, Version=v{Version}",
                workstation.PcId, authoritativeVersionNumber);

            return Result.Success(new ConfigurationSyncResult
            {
                Status = ConfigurationSyncStatus.FullPackage,
                VersionNumber = authoritativeVersionNumber,
                Version = $"v{authoritativeVersionNumber}",
                Hash = hashHex,
                Signature = signatureBase64,
                KeyId = keyId,
                PayloadType = "Full",
                Payload = parsedPayload,
                TargetClient = workstation.PcId,
                TargetGroup = resolution.AppliedSources.FirstOrDefault(s => s.TargetType == "Group")?.PackageName
            });
        }

        private async Task<ConfigurationSyncResult?> TryBuildDeltaPackageAsync(
            Workstation workstation,
            long clientVersionNumber,
            long targetVersionNumber,
            string targetEffectiveJson,
            string targetHashHex,
            string targetSignatureBase64,
            string? keyId,
            ConfigurationResolutionResult resolution,
            CancellationToken cancellationToken)
        {
            try
            {
                // Retrieve version history packages for scope if available
                string pkgName = resolution.AppliedSources.FirstOrDefault()?.PackageName ?? "default";
                var basePackage = await _packageRepository.GetByVersionNumberAsync(pkgName, clientVersionNumber, cancellationToken);
                if (basePackage == null)
                {
                    return null;
                }

                string baseContent = basePackage.Content;
                if (basePackage.PayloadType == ConfigurationPayloadType.Delta)
                {
                    // Reconstruct base content if it was a delta package
                    var reconstructHandler = new ReconstructConfigurationCommandHandler(_packageRepository, _deltaEngine);
                    var reconResult = await reconstructHandler.HandleAsync(new ReconstructConfigurationCommand(pkgName, clientVersionNumber), cancellationToken);
                    if (!reconResult.IsSuccess || string.IsNullOrWhiteSpace(reconResult.Value))
                    {
                        return null;
                    }
                    baseContent = reconResult.Value;
                }

                // Compute JSON delta operations from baseContent to targetEffectiveJson
                var deltas = _deltaEngine.ComputeDelta(baseContent, targetEffectiveJson);
                if (deltas == null || deltas.Count == 0)
                {
                    return null;
                }

                // Verify Delta Safety: apply deltas to baseContent and verify it equals targetEffectiveJson
                string recomputedTargetJson = _deltaEngine.ApplyDelta(baseContent, deltas);
                byte[] canonicalRecomputed = _canonicalSerializer.SerializeToCanonicalBytes(recomputedTargetJson);
                byte[] canonicalExpected = _canonicalSerializer.SerializeToCanonicalBytes(targetEffectiveJson);

                if (!_hashService.VerifyHash(canonicalRecomputed, _hashService.ComputeHash(canonicalExpected)))
                {
                    _logger.LogWarning("Delta validation failed for Workstation '{PcId}': Recomputed target content does not match expected target content.", workstation.PcId);
                    return null;
                }

                return new ConfigurationSyncResult
                {
                    Status = ConfigurationSyncStatus.DeltaPackage,
                    VersionNumber = targetVersionNumber,
                    BaseVersionNumber = clientVersionNumber,
                    Version = $"v{targetVersionNumber}",
                    Hash = targetHashHex,
                    Signature = targetSignatureBase64,
                    KeyId = keyId,
                    PayloadType = "Delta",
                    Payload = deltas,
                    TargetClient = workstation.PcId,
                    TargetGroup = resolution.AppliedSources.FirstOrDefault(s => s.TargetType == "Group")?.PackageName
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build delta package for Workstation '{PcId}' from v{ClientVersion} to v{TargetVersion}.",
                    workstation.PcId, clientVersionNumber, targetVersionNumber);
                return null;
            }
        }
    }
}

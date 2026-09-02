using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

#nullable enable

namespace Sayra.Backend.Application.Configuration
{
    // -------------------------------------------------------------------
    // DTOs
    // -------------------------------------------------------------------
    public class ConfigurationPublicationDto
    {
        public Guid Id { get; set; }
        public Guid ConfigurationPackageId { get; set; }
        public long VersionNumber { get; set; }
        public string Version { get; set; } = string.Empty;
        public Guid ConfigurationTargetId { get; set; }
        public Guid OrganizationId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string IssuedBy { get; set; } = string.Empty;
        public DateTime? PublishedAt { get; set; }
        public DateTime? ActivatedAt { get; set; }
        public DateTime? SupersededAt { get; set; }
        public Guid? SupersededByPublicationId { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? RevokedBy { get; set; }
        public string? RevocationReason { get; set; }
        public string? CorrelationId { get; set; }
        public string? Notes { get; set; }
        public bool IsRollback { get; set; }
        public long? SourceVersionNumber { get; set; }
        public long? FailedVersionNumber { get; set; }
        public Guid? SourcePublicationId { get; set; }
        public string ConfigurationHash { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public string SignatureAlgorithm { get; set; } = string.Empty;
        public string SigningKeyId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        public static ConfigurationPublicationDto FromDomain(ConfigurationPublication entity)
        {
            return new ConfigurationPublicationDto
            {
                Id = entity.Id,
                ConfigurationPackageId = entity.ConfigurationPackageId,
                VersionNumber = entity.VersionNumber,
                Version = entity.Version,
                ConfigurationTargetId = entity.ConfigurationTargetId,
                OrganizationId = entity.OrganizationId,
                Status = entity.Status.ToString(),
                IssuedBy = entity.IssuedBy,
                PublishedAt = entity.PublishedAt,
                ActivatedAt = entity.ActivatedAt,
                SupersededAt = entity.SupersededAt,
                SupersededByPublicationId = entity.SupersededByPublicationId,
                RevokedAt = entity.RevokedAt,
                RevokedBy = entity.RevokedBy,
                RevocationReason = entity.RevocationReason,
                CorrelationId = entity.CorrelationId,
                Notes = entity.Notes,
                IsRollback = entity.IsRollback,
                SourceVersionNumber = entity.SourceVersionNumber,
                FailedVersionNumber = entity.FailedVersionNumber,
                SourcePublicationId = entity.SourcePublicationId,
                ConfigurationHash = entity.ConfigurationHash,
                Signature = entity.Signature,
                SignatureAlgorithm = entity.SignatureAlgorithm,
                SigningKeyId = entity.SigningKeyId,
                CreatedAt = entity.CreatedAt
            };
        }
    }

    // -------------------------------------------------------------------
    // 1. Prepare / Create Publication Command
    // -------------------------------------------------------------------
    public record PreparePublicationCommand(
        Guid ConfigurationPackageId,
        Guid ConfigurationTargetId,
        string? Actor = null,
        string? Notes = null,
        string? CorrelationId = null,
        string? IdempotencyKey = null) : ICommand<ConfigurationPublicationDto>;

    public class PreparePublicationCommandHandler : ICommandHandler<PreparePublicationCommand, ConfigurationPublicationDto>
    {
        private readonly IConfigurationPackageRepository _packageRepository;
        private readonly IConfigurationTargetRepository _targetRepository;
        private readonly IConfigurationAssignmentRepository _assignmentRepository;
        private readonly IConfigurationPublicationRepository _publicationRepository;
        private readonly IConfigurationSigningService _signingService;
        private readonly IConfigurationValidator _validator;
        private readonly IUnitOfWork _unitOfWork;

        public PreparePublicationCommandHandler(
            IConfigurationPackageRepository packageRepository,
            IConfigurationTargetRepository targetRepository,
            IConfigurationAssignmentRepository assignmentRepository,
            IConfigurationPublicationRepository publicationRepository,
            IConfigurationSigningService signingService,
            IConfigurationValidator validator,
            IUnitOfWork unitOfWork)
        {
            _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
            _targetRepository = targetRepository ?? throw new ArgumentNullException(nameof(targetRepository));
            _assignmentRepository = assignmentRepository ?? throw new ArgumentNullException(nameof(assignmentRepository));
            _publicationRepository = publicationRepository ?? throw new ArgumentNullException(nameof(publicationRepository));
            _signingService = signingService ?? throw new ArgumentNullException(nameof(signingService));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<ConfigurationPublicationDto>> HandleAsync(PreparePublicationCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null) return Result.Failure<ConfigurationPublicationDto>("NULL_COMMAND", "Command cannot be null.");

            if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                var existing = await _publicationRepository.GetByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken);
                if (existing != null)
                {
                    return Result.Success(ConfigurationPublicationDto.FromDomain(existing));
                }
            }

            var package = await _packageRepository.GetByIdAsync(command.ConfigurationPackageId, track: false, cancellationToken);
            if (package == null)
            {
                return Result.Failure<ConfigurationPublicationDto>("ConfigurationVersionNotFound", $"Package '{command.ConfigurationPackageId}' not found.");
            }

            if (!package.IsActive)
            {
                return Result.Failure<ConfigurationPublicationDto>("ConfigurationRevoked", $"Package '{package.Version}' is revoked/inactive.");
            }

            if (string.IsNullOrWhiteSpace(package.Signature) || string.IsNullOrWhiteSpace(package.SigningKeyId))
            {
                return Result.Failure<ConfigurationPublicationDto>("ConfigurationNotSigned", $"Package '{package.Version}' is not digitally signed.");
            }

            var target = await _targetRepository.GetByIdAsync(command.ConfigurationTargetId, track: false, cancellationToken);
            if (target == null)
            {
                return Result.Failure<ConfigurationPublicationDto>("TargetNotFound", $"Target '{command.ConfigurationTargetId}' not found.");
            }

            // Stage 06-06 Signature verification check
            var verifyResult = await _signingService.VerifyPackageAsync(package.Content, package.ConfigurationHash ?? "", package.Signature, package.SigningKeyId, cancellationToken);
            if (!verifyResult.IsValid)
            {
                return Result.Failure<ConfigurationPublicationDto>("InvalidConfigurationSignature", $"Cryptographic signature verification failed: {verifyResult.FailureReason}");
            }

            // Stage 06-02 Validation check
            var valResult = _validator.Validate(package.Content);
            if (!valResult.IsValid)
            {
                return Result.Failure<ConfigurationPublicationDto>("ConfigurationNotValidated", "Package content failed validation rules.");
            }

            // Ensure assignment exists
            var assignment = await _assignmentRepository.GetAssignmentByPackageAndTargetAsync(package.Id, target.Id, cancellationToken);
            if (assignment == null)
            {
                assignment = ConfigurationAssignment.Create(package.Id, target.Id, command.Actor ?? "system");
                await _assignmentRepository.AddAsync(assignment, cancellationToken);
            }
            else if (!assignment.IsActive)
            {
                assignment.Reassign(command.Actor ?? "system");
            }

            var publication = ConfigurationPublication.Create(
                packageId: package.Id,
                versionNumber: package.VersionNumber,
                version: package.Version,
                targetId: target.Id,
                organizationId: target.OrganizationId,
                hash: package.ConfigurationHash!,
                signature: package.Signature,
                keyId: package.SigningKeyId,
                algorithm: package.SignatureAlgorithm ?? "RSA-SHA256",
                issuedBy: command.Actor ?? "system",
                notes: command.Notes,
                correlationId: command.CorrelationId,
                idempotencyKey: command.IdempotencyKey);

            await _publicationRepository.AddAsync(publication, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(ConfigurationPublicationDto.FromDomain(publication));
        }
    }

    // -------------------------------------------------------------------
    // 2. Publish Configuration Command
    // -------------------------------------------------------------------
    public record PublishConfigurationCommand(
        Guid ConfigurationPackageId,
        Guid ConfigurationTargetId,
        string? Actor = null,
        string? Notes = null,
        string? CorrelationId = null,
        string? IdempotencyKey = null) : ICommand<ConfigurationPublicationDto>;

    public class PublishConfigurationCommandHandler : ICommandHandler<PublishConfigurationCommand, ConfigurationPublicationDto>
    {
        private readonly IConfigurationPackageRepository _packageRepository;
        private readonly IConfigurationTargetRepository _targetRepository;
        private readonly IConfigurationAssignmentRepository _assignmentRepository;
        private readonly IConfigurationPublicationRepository _publicationRepository;
        private readonly IConfigurationSigningService _signingService;
        private readonly IConfigurationValidator _validator;
        private readonly IUnitOfWork _unitOfWork;

        public PublishConfigurationCommandHandler(
            IConfigurationPackageRepository packageRepository,
            IConfigurationTargetRepository targetRepository,
            IConfigurationAssignmentRepository assignmentRepository,
            IConfigurationPublicationRepository publicationRepository,
            IConfigurationSigningService signingService,
            IConfigurationValidator validator,
            IUnitOfWork unitOfWork)
        {
            _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
            _targetRepository = targetRepository ?? throw new ArgumentNullException(nameof(targetRepository));
            _assignmentRepository = assignmentRepository ?? throw new ArgumentNullException(nameof(assignmentRepository));
            _publicationRepository = publicationRepository ?? throw new ArgumentNullException(nameof(publicationRepository));
            _signingService = signingService ?? throw new ArgumentNullException(nameof(signingService));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<ConfigurationPublicationDto>> HandleAsync(PublishConfigurationCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null) return Result.Failure<ConfigurationPublicationDto>("NULL_COMMAND", "Command cannot be null.");

            if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                var existingKey = await _publicationRepository.GetByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken);
                if (existingKey != null && (existingKey.Status == ConfigurationLifecycleState.Published || existingKey.Status == ConfigurationLifecycleState.Active))
                {
                    return Result.Success(ConfigurationPublicationDto.FromDomain(existingKey));
                }
            }

            return await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var package = await _packageRepository.GetByIdAsync(command.ConfigurationPackageId, track: false, cancellationToken);
                if (package == null)
                {
                    return Result.Failure<ConfigurationPublicationDto>("ConfigurationVersionNotFound", $"Package '{command.ConfigurationPackageId}' not found.");
                }

                if (!package.IsActive)
                {
                    return Result.Failure<ConfigurationPublicationDto>("ConfigurationRevoked", $"Package '{package.Version}' is revoked or inactive.");
                }

                if (string.IsNullOrWhiteSpace(package.Signature) || string.IsNullOrWhiteSpace(package.SigningKeyId))
                {
                    return Result.Failure<ConfigurationPublicationDto>("ConfigurationNotSigned", $"Package '{package.Version}' is not digitally signed.");
                }

                var target = await _targetRepository.GetByIdAsync(command.ConfigurationTargetId, track: false, cancellationToken);
                if (target == null)
                {
                    return Result.Failure<ConfigurationPublicationDto>("TargetNotFound", $"Target '{command.ConfigurationTargetId}' not found.");
                }

                // Verify Cryptographic Signature
                var verifyResult = await _signingService.VerifyPackageAsync(package.Content, package.ConfigurationHash ?? "", package.Signature, package.SigningKeyId, cancellationToken);
                if (!verifyResult.IsValid)
                {
                    return Result.Failure<ConfigurationPublicationDto>("InvalidConfigurationSignature", $"Cryptographic signature verification failed: {verifyResult.FailureReason}");
                }

                // Verify Content Validation
                var valResult = _validator.Validate(package.Content);
                if (!valResult.IsValid)
                {
                    return Result.Failure<ConfigurationPublicationDto>("ConfigurationNotValidated", "Package content failed validation rules.");
                }

                // Ensure assignment exists
                var assignment = await _assignmentRepository.GetAssignmentByPackageAndTargetAsync(package.Id, target.Id, cancellationToken);
                if (assignment == null)
                {
                    assignment = ConfigurationAssignment.Create(package.Id, target.Id, command.Actor ?? "system");
                    await _assignmentRepository.AddAsync(assignment, cancellationToken);
                }
                else if (!assignment.IsActive)
                {
                    assignment.Reassign(command.Actor ?? "system");
                }

                // Check existing publication for this package & target
                var publication = await _publicationRepository.GetByPackageAndTargetAsync(package.Id, target.Id, cancellationToken);
                if (publication == null)
                {
                    publication = ConfigurationPublication.Create(
                        packageId: package.Id,
                        versionNumber: package.VersionNumber,
                        version: package.Version,
                        targetId: target.Id,
                        organizationId: target.OrganizationId,
                        hash: package.ConfigurationHash!,
                        signature: package.Signature,
                        keyId: package.SigningKeyId,
                        algorithm: package.SignatureAlgorithm ?? "RSA-SHA256",
                        issuedBy: command.Actor ?? "system",
                        notes: command.Notes,
                        correlationId: command.CorrelationId,
                        idempotencyKey: command.IdempotencyKey);

                    await _publicationRepository.AddAsync(publication, cancellationToken);
                }

                if (publication.Status == ConfigurationLifecycleState.Revoked)
                {
                    return Result.Failure<ConfigurationPublicationDto>("ConfigurationRevoked", "Cannot publish a revoked publication.");
                }

                if (publication.Status == ConfigurationLifecycleState.Signed)
                {
                    publication.Publish(command.Actor ?? "system");
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Success(ConfigurationPublicationDto.FromDomain(publication));
            }, cancellationToken);
        }
    }

    // -------------------------------------------------------------------
    // 3. Activate Configuration Command
    // -------------------------------------------------------------------
    public record ActivateConfigurationCommand(
        Guid PublicationId,
        string? Actor = null) : ICommand<ConfigurationPublicationDto>;

    public class ActivateConfigurationCommandHandler : ICommandHandler<ActivateConfigurationCommand, ConfigurationPublicationDto>
    {
        private readonly IConfigurationPublicationRepository _publicationRepository;
        private readonly IConfigurationPackageRepository _packageRepository;
        private readonly IConfigurationSigningService _signingService;
        private readonly IUnitOfWork _unitOfWork;

        public ActivateConfigurationCommandHandler(
            IConfigurationPublicationRepository publicationRepository,
            IConfigurationPackageRepository packageRepository,
            IConfigurationSigningService signingService,
            IUnitOfWork unitOfWork)
        {
            _publicationRepository = publicationRepository ?? throw new ArgumentNullException(nameof(publicationRepository));
            _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
            _signingService = signingService ?? throw new ArgumentNullException(nameof(signingService));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<ConfigurationPublicationDto>> HandleAsync(ActivateConfigurationCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null) return Result.Failure<ConfigurationPublicationDto>("NULL_COMMAND", "Command cannot be null.");

            return await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var publication = await _publicationRepository.GetByIdAsync(command.PublicationId, track: true, cancellationToken);
                if (publication == null)
                {
                    return Result.Failure<ConfigurationPublicationDto>("ConfigurationNotFound", $"Publication '{command.PublicationId}' not found.");
                }

                if (publication.Status == ConfigurationLifecycleState.Active)
                {
                    return Result.Success(ConfigurationPublicationDto.FromDomain(publication));
                }

                if (publication.Status == ConfigurationLifecycleState.Revoked)
                {
                    return Result.Failure<ConfigurationPublicationDto>("ConfigurationRevoked", "Cannot activate a revoked publication.");
                }

                if (publication.Status == ConfigurationLifecycleState.Superseded)
                {
                    return Result.Failure<ConfigurationPublicationDto>("ConfigurationSuperseded", "Cannot activate a superseded publication.");
                }

                if (publication.Status == ConfigurationLifecycleState.Draft || publication.Status == ConfigurationLifecycleState.Validated)
                {
                    return Result.Failure<ConfigurationPublicationDto>("InvalidLifecycleTransition", $"Publication in state '{publication.Status}' cannot be directly activated.");
                }

                if (publication.Status == ConfigurationLifecycleState.Signed)
                {
                    publication.Publish(command.Actor ?? "system");
                }

                // Verify cryptographic signature before activating
                var package = await _packageRepository.GetByIdAsync(publication.ConfigurationPackageId, track: false, cancellationToken);
                if (package == null || !package.IsActive)
                {
                    return Result.Failure<ConfigurationPublicationDto>("ConfigurationRevoked", "Associated configuration package is invalid or revoked.");
                }

                var verifyResult = await _signingService.VerifyPackageAsync(package.Content, publication.ConfigurationHash, publication.Signature, publication.SigningKeyId, cancellationToken);
                if (!verifyResult.IsValid)
                {
                    return Result.Failure<ConfigurationPublicationDto>("InvalidConfigurationSignature", $"Signature verification failed during activation: {verifyResult.FailureReason}");
                }

                // Supersede existing ACTIVE publication for this target
                var currentActive = await _publicationRepository.GetActivePublicationForTargetAsync(publication.ConfigurationTargetId, cancellationToken);
                if (currentActive != null && currentActive.Id != publication.Id)
                {
                    currentActive.Supersede(publication.Id);
                }

                publication.Activate(command.Actor ?? "system");

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Success(ConfigurationPublicationDto.FromDomain(publication));
            }, cancellationToken);
        }
    }

    // -------------------------------------------------------------------
    // 4. Revoke Configuration Command
    // -------------------------------------------------------------------
    public record RevokeConfigurationCommand(
        Guid PublicationId,
        string Reason,
        string? Actor = null) : ICommand<ConfigurationPublicationDto>;

    public class RevokeConfigurationCommandHandler : ICommandHandler<RevokeConfigurationCommand, ConfigurationPublicationDto>
    {
        private readonly IConfigurationPublicationRepository _publicationRepository;
        private readonly IConfigurationPackageRepository _packageRepository;
        private readonly IUnitOfWork _unitOfWork;

        public RevokeConfigurationCommandHandler(
            IConfigurationPublicationRepository publicationRepository,
            IConfigurationPackageRepository packageRepository,
            IUnitOfWork unitOfWork)
        {
            _publicationRepository = publicationRepository ?? throw new ArgumentNullException(nameof(publicationRepository));
            _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<ConfigurationPublicationDto>> HandleAsync(RevokeConfigurationCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null) return Result.Failure<ConfigurationPublicationDto>("NULL_COMMAND", "Command cannot be null.");
            if (string.IsNullOrWhiteSpace(command.Reason)) return Result.Failure<ConfigurationPublicationDto>("REVOCATION_REASON_REQUIRED", "Revocation reason is required.");

            return await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var publication = await _publicationRepository.GetByIdAsync(command.PublicationId, track: true, cancellationToken);
                if (publication == null)
                {
                    return Result.Failure<ConfigurationPublicationDto>("ConfigurationNotFound", $"Publication '{command.PublicationId}' not found.");
                }

                if (publication.Status == ConfigurationLifecycleState.Revoked)
                {
                    return Result.Success(ConfigurationPublicationDto.FromDomain(publication));
                }

                publication.Revoke(command.Actor ?? "system", command.Reason);

                // Mark associated package inactive
                var package = await _packageRepository.GetByIdAsync(publication.ConfigurationPackageId, track: true, cancellationToken);
                if (package != null)
                {
                    package.IsActive = false;
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Success(ConfigurationPublicationDto.FromDomain(publication));
            }, cancellationToken);
        }
    }

    // -------------------------------------------------------------------
    // 5. Rollback Configuration Command
    // -------------------------------------------------------------------
    public record RollbackConfigurationCommand(
        Guid ConfigurationTargetId,
        long KnownGoodVersionNumber,
        long? FailedVersionNumber = null,
        string? PackageName = null,
        string Reason = "Administrative Rollback",
        string? Actor = null,
        string? CorrelationId = null,
        string? IdempotencyKey = null) : ICommand<ConfigurationPublicationDto>;

    public class RollbackConfigurationCommandHandler : ICommandHandler<RollbackConfigurationCommand, ConfigurationPublicationDto>
    {
        private readonly IConfigurationTargetRepository _targetRepository;
        private readonly IConfigurationPackageRepository _packageRepository;
        private readonly IConfigurationAssignmentRepository _assignmentRepository;
        private readonly IConfigurationPublicationRepository _publicationRepository;
        private readonly IConfigurationSigningService _signingService;
        private readonly IConfigurationValidator _validator;
        private readonly IConfigurationDeltaEngine _deltaEngine;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ReconstructConfigurationCommandHandler _reconstructHandler;

        public RollbackConfigurationCommandHandler(
            IConfigurationTargetRepository targetRepository,
            IConfigurationPackageRepository packageRepository,
            IConfigurationAssignmentRepository assignmentRepository,
            IConfigurationPublicationRepository publicationRepository,
            IConfigurationSigningService signingService,
            IConfigurationValidator validator,
            IConfigurationDeltaEngine deltaEngine,
            IUnitOfWork unitOfWork)
        {
            _targetRepository = targetRepository ?? throw new ArgumentNullException(nameof(targetRepository));
            _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
            _assignmentRepository = assignmentRepository ?? throw new ArgumentNullException(nameof(assignmentRepository));
            _publicationRepository = publicationRepository ?? throw new ArgumentNullException(nameof(publicationRepository));
            _signingService = signingService ?? throw new ArgumentNullException(nameof(signingService));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _deltaEngine = deltaEngine ?? throw new ArgumentNullException(nameof(deltaEngine));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _reconstructHandler = new ReconstructConfigurationCommandHandler(packageRepository, deltaEngine);
        }

        public async Task<Result<ConfigurationPublicationDto>> HandleAsync(RollbackConfigurationCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null) return Result.Failure<ConfigurationPublicationDto>("NULL_COMMAND", "Command cannot be null.");

            if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                var existing = await _publicationRepository.GetByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken);
                if (existing != null)
                {
                    return Result.Success(ConfigurationPublicationDto.FromDomain(existing));
                }
            }

            return await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var target = await _targetRepository.GetByIdAsync(command.ConfigurationTargetId, track: false, cancellationToken);
                if (target == null)
                {
                    return Result.Failure<ConfigurationPublicationDto>("RollbackTargetInvalid", $"Target '{command.ConfigurationTargetId}' not found.");
                }

                var currentActivePub = await _publicationRepository.GetActivePublicationForTargetAsync(target.Id, cancellationToken);
                long failedVerNumber = command.FailedVersionNumber ?? currentActivePub?.VersionNumber ?? 0;

                // Resolve package family name dynamically
                string pkgName = command.PackageName?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(pkgName) && currentActivePub != null)
                {
                    var activePkg = await _packageRepository.GetByIdAsync(currentActivePub.ConfigurationPackageId, track: false, cancellationToken);
                    if (activePkg != null)
                    {
                        pkgName = activePkg.Name;
                    }
                }
                if (string.IsNullOrWhiteSpace(pkgName))
                {
                    pkgName = "default";
                }

                // Load source known-good package
                var knownGoodPkg = await _packageRepository.GetByVersionNumberAsync(pkgName, command.KnownGoodVersionNumber, cancellationToken);
                if (knownGoodPkg == null)
                {
                    return Result.Failure<ConfigurationPublicationDto>("RollbackVersionInvalid", $"Known-good version v{command.KnownGoodVersionNumber} not found for scope '{pkgName}'.");
                }

                // Reconstruct full content of known-good version if delta
                var reconResult = await _reconstructHandler.HandleAsync(new ReconstructConfigurationCommand(pkgName, command.KnownGoodVersionNumber), cancellationToken);
                if (!reconResult.IsSuccess)
                {
                    return Result.Failure<ConfigurationPublicationDto>("RollbackVersionInvalid", $"Failed to reconstruct known-good version v{command.KnownGoodVersionNumber}: {reconResult.ErrorMessage}");
                }

                string reconstructedContent = reconResult.Value!;

                // Validate reconstructed content
                var valResult = _validator.Validate(reconstructedContent);
                if (!valResult.IsValid)
                {
                    return Result.Failure<ConfigurationPublicationDto>("ConfigurationNotValidated", "Reconstructed rollback content failed validation.");
                }

                // Determine next version number
                var latestPkg = await _packageRepository.GetLatestVersionAsync(pkgName, cancellationToken);
                long nextVersionNumber = (latestPkg?.VersionNumber ?? 0) + 1;

                // Create NEW rollback package (immutable version)
                var rollbackPackage = ConfigurationPackage.CreateFull(
                    name: knownGoodPkg.Name,
                    versionNumber: nextVersionNumber,
                    content: reconstructedContent,
                    schemaVersion: knownGoodPkg.SchemaVersion,
                    issuedBy: command.Actor ?? "system");

                // Digitally sign new rollback package using Stage 06-06
                var signResult = await _signingService.SignPackageAsync(reconstructedContent, cancellationToken: cancellationToken);
                rollbackPackage.SetCryptographicSignature(signResult.Hash, signResult.Signature, signResult.Algorithm, signResult.KeyId);

                await _packageRepository.AddAsync(rollbackPackage, cancellationToken);

                // Create assignment
                var assignment = ConfigurationAssignment.Create(rollbackPackage.Id, target.Id, command.Actor ?? "system");
                await _assignmentRepository.AddAsync(assignment, cancellationToken);

                // Create NEW publication
                var rollbackPublication = ConfigurationPublication.Create(
                    packageId: rollbackPackage.Id,
                    versionNumber: rollbackPackage.VersionNumber,
                    version: rollbackPackage.Version,
                    targetId: target.Id,
                    organizationId: target.OrganizationId,
                    hash: rollbackPackage.ConfigurationHash!,
                    signature: rollbackPackage.Signature!,
                    keyId: rollbackPackage.SigningKeyId!,
                    algorithm: rollbackPackage.SignatureAlgorithm ?? "RSA-SHA256",
                    issuedBy: command.Actor ?? "system",
                    notes: command.Reason,
                    correlationId: command.CorrelationId,
                    idempotencyKey: command.IdempotencyKey,
                    isRollback: true,
                    sourceVersionNumber: command.KnownGoodVersionNumber,
                    failedVersionNumber: failedVerNumber,
                    sourcePublicationId: currentActivePub?.Id);

                rollbackPublication.Publish(command.Actor ?? "system");

                if (currentActivePub != null)
                {
                    currentActivePub.Supersede(rollbackPublication.Id);
                }

                rollbackPublication.Activate(command.Actor ?? "system");

                await _publicationRepository.AddAsync(rollbackPublication, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Success(ConfigurationPublicationDto.FromDomain(rollbackPublication));
            }, cancellationToken);
        }
    }

    // -------------------------------------------------------------------
    // 6. Queries
    // -------------------------------------------------------------------
    public record GetPublicationByIdQuery(Guid PublicationId) : IQuery<ConfigurationPublicationDto?>;

    public class GetPublicationByIdQueryHandler : IQueryHandler<GetPublicationByIdQuery, ConfigurationPublicationDto?>
    {
        private readonly IConfigurationPublicationRepository _repository;

        public GetPublicationByIdQueryHandler(IConfigurationPublicationRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<Result<ConfigurationPublicationDto?>> HandleAsync(GetPublicationByIdQuery query, CancellationToken cancellationToken = default)
        {
            if (query == null) return Result.Failure<ConfigurationPublicationDto?>("NULL_QUERY", "Query cannot be null.");

            var pub = await _repository.GetByIdAsync(query.PublicationId, track: false, cancellationToken);
            if (pub == null)
            {
                return Result.Success<ConfigurationPublicationDto?>(null);
            }

            return Result.Success<ConfigurationPublicationDto?>(ConfigurationPublicationDto.FromDomain(pub));
        }
    }

    public record GetActiveTargetPublicationQuery(Guid TargetId) : IQuery<ConfigurationPublicationDto?>;

    public class GetActiveTargetPublicationQueryHandler : IQueryHandler<GetActiveTargetPublicationQuery, ConfigurationPublicationDto?>
    {
        private readonly IConfigurationPublicationRepository _repository;

        public GetActiveTargetPublicationQueryHandler(IConfigurationPublicationRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<Result<ConfigurationPublicationDto?>> HandleAsync(GetActiveTargetPublicationQuery query, CancellationToken cancellationToken = default)
        {
            if (query == null) return Result.Failure<ConfigurationPublicationDto?>("NULL_QUERY", "Query cannot be null.");

            var pub = await _repository.GetActivePublicationForTargetAsync(query.TargetId, cancellationToken);
            if (pub == null)
            {
                return Result.Success<ConfigurationPublicationDto?>(null);
            }

            return Result.Success<ConfigurationPublicationDto?>(ConfigurationPublicationDto.FromDomain(pub));
        }
    }
}

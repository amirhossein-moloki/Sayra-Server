using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Configuration.Models;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Configuration
{
    public record ValidateConfigurationCommand(
        string? RawPayload = null,
        SayraConfigurationSchema? SchemaModel = null) : ICommand<ConfigurationValidationResult>;

    public class ValidateConfigurationCommandHandler : ICommandHandler<ValidateConfigurationCommand, ConfigurationValidationResult>
    {
        private readonly IConfigurationValidator _validator;

        public ValidateConfigurationCommandHandler(IConfigurationValidator validator)
        {
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        public Task<Result<ConfigurationValidationResult>> HandleAsync(ValidateConfigurationCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                return Task.FromResult(Result.Failure<ConfigurationValidationResult>("NULL_COMMAND", "Command cannot be null."));
            }

            if (command.SchemaModel != null)
            {
                var result = _validator.Validate(command.SchemaModel);
                return Task.FromResult(Result.Success(result));
            }

            if (command.RawPayload != null)
            {
                var result = _validator.Validate(command.RawPayload);
                return Task.FromResult(Result.Success(result));
            }

            var emptyResult = ConfigurationValidationResult.Failure("", "PAYLOAD_EMPTY", "Either RawPayload or SchemaModel must be provided.");
            return Task.FromResult(Result.Success(emptyResult));
        }
    }

    public record NormalizeConfigurationCommand(
        string? RawPayload = null,
        SayraConfigurationSchema? SchemaModel = null) : ICommand<string>;

    public class NormalizeConfigurationCommandHandler : ICommandHandler<NormalizeConfigurationCommand, string>
    {
        private readonly IConfigurationNormalizer _normalizer;

        public NormalizeConfigurationCommandHandler(IConfigurationNormalizer normalizer)
        {
            _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        }

        public Task<Result<string>> HandleAsync(NormalizeConfigurationCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                return Task.FromResult(Result.Failure<string>("NULL_COMMAND", "Command cannot be null."));
            }

            try
            {
                if (command.SchemaModel != null)
                {
                    var normalizedJson = _normalizer.NormalizeToJson(command.SchemaModel);
                    return Task.FromResult(Result.Success(normalizedJson));
                }

                if (command.RawPayload != null)
                {
                    var normalizedJson = _normalizer.NormalizeToJson(command.RawPayload);
                    return Task.FromResult(Result.Success(normalizedJson));
                }

                return Task.FromResult(Result.Failure<string>("INVALID_ARGUMENTS", "Either RawPayload or SchemaModel must be provided for normalization."));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result.Failure<string>("NORMALIZATION_FAILED", ex.Message));
            }
        }
    }

    public record CreateFullConfigurationVersionCommand(
        string Name,
        string RawPayload,
        string SchemaVersion = "1.0",
        string IssuedBy = "system") : ICommand<ConfigurationPackage>;

    public class CreateFullConfigurationVersionCommandHandler : ICommandHandler<CreateFullConfigurationVersionCommand, ConfigurationPackage>
    {
        private readonly IConfigurationPackageRepository _repository;
        private readonly IConfigurationNormalizer _normalizer;
        private readonly IConfigurationValidator _validator;
        private readonly IUnitOfWork _unitOfWork;

        public CreateFullConfigurationVersionCommandHandler(
            IConfigurationPackageRepository repository,
            IConfigurationNormalizer normalizer,
            IConfigurationValidator validator,
            IUnitOfWork unitOfWork)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<ConfigurationPackage>> HandleAsync(CreateFullConfigurationVersionCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                return Result.Failure<ConfigurationPackage>("NULL_COMMAND", "Command cannot be null.");
            }

            var name = string.IsNullOrWhiteSpace(command.Name) ? "default" : command.Name.Trim();

            // Validate raw payload
            var validation = _validator.Validate(command.RawPayload);
            if (!validation.IsValid)
            {
                var errors = string.Join("; ", validation.Errors.Select(e => $"[{e.Path}] {e.Code}: {e.Message}"));
                return Result.Failure<ConfigurationPackage>("TargetConfigurationInvalid", $"Configuration payload validation failed: {errors}");
            }

            string normalizedJson;
            try
            {
                normalizedJson = _normalizer.NormalizeToJson(command.RawPayload);
            }
            catch (Exception ex)
            {
                return Result.Failure<ConfigurationPackage>("NORMALIZATION_FAILED", ex.Message);
            }

            return await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var latest = await _repository.GetLatestVersionAsync(name, cancellationToken);
                long nextVersion = (latest?.VersionNumber ?? 0) + 1;

                var package = ConfigurationPackage.CreateFull(
                    name: name,
                    versionNumber: nextVersion,
                    content: normalizedJson,
                    schemaVersion: command.SchemaVersion,
                    issuedBy: command.IssuedBy);

                await _repository.AddAsync(package, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Success(package);
            }, cancellationToken);
        }
    }

    public record CreateDeltaConfigurationVersionCommand(
        string Name,
        long BaseVersionNumber,
        string RawPayload,
        string SchemaVersion = "1.0",
        string IssuedBy = "system") : ICommand<ConfigurationPackage>;

    public class CreateDeltaConfigurationVersionCommandHandler : ICommandHandler<CreateDeltaConfigurationVersionCommand, ConfigurationPackage>
    {
        private readonly IConfigurationPackageRepository _repository;
        private readonly IConfigurationDeltaEngine _deltaEngine;
        private readonly IConfigurationValidator _validator;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ReconstructConfigurationCommandHandler _reconstructHandler;

        public CreateDeltaConfigurationVersionCommandHandler(
            IConfigurationPackageRepository repository,
            IConfigurationDeltaEngine deltaEngine,
            IConfigurationValidator validator,
            IUnitOfWork unitOfWork)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _deltaEngine = deltaEngine ?? throw new ArgumentNullException(nameof(deltaEngine));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _reconstructHandler = new ReconstructConfigurationCommandHandler(repository, deltaEngine);
        }

        public async Task<Result<ConfigurationPackage>> HandleAsync(CreateDeltaConfigurationVersionCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                return Result.Failure<ConfigurationPackage>("NULL_COMMAND", "Command cannot be null.");
            }

            var name = string.IsNullOrWhiteSpace(command.Name) ? "default" : command.Name.Trim();

            if (command.BaseVersionNumber <= 0)
            {
                return Result.Failure<ConfigurationPackage>("InvalidBaseVersion", "Base version number must be greater than 0.");
            }

            // Reconstruct base full payload (works whether base is Full or Delta)
            var reconResult = await _reconstructHandler.HandleAsync(new ReconstructConfigurationCommand(name, command.BaseVersionNumber), cancellationToken);
            if (!reconResult.IsSuccess)
            {
                return Result.Failure<ConfigurationPackage>("InvalidBaseVersion", reconResult.ErrorMessage ?? "Failed to reconstruct base version.");
            }

            string baseFullContent = reconResult.Value ?? "{}";

            // Try applying delta (Apply-then-Normalize-then-Validate)
            string targetNormalizedContent;
            try
            {
                targetNormalizedContent = _deltaEngine.ApplyDelta(baseFullContent, command.RawPayload);
            }
            catch (Exception ex)
            {
                return Result.Failure<ConfigurationPackage>("DeltaApplicationFailed", ex.Message);
            }

            return await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var latest = await _repository.GetLatestVersionAsync(name, cancellationToken);
                long nextVersion = (latest?.VersionNumber ?? 0) + 1;

                if (command.BaseVersionNumber >= nextVersion)
                {
                    return Result.Failure<ConfigurationPackage>("InvalidBaseVersion", $"Base version ({command.BaseVersionNumber}) must be smaller than next target version ({nextVersion}).");
                }

                var package = ConfigurationPackage.CreateDelta(
                    name: name,
                    versionNumber: nextVersion,
                    baseVersionNumber: command.BaseVersionNumber,
                    content: command.RawPayload,
                    schemaVersion: command.SchemaVersion,
                    issuedBy: command.IssuedBy);

                await _repository.AddAsync(package, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Success(package);
            }, cancellationToken);
        }
    }

    public record ReconstructConfigurationCommand(
        string Name,
        long TargetVersionNumber) : ICommand<string>;

    public class ReconstructConfigurationCommandHandler : ICommandHandler<ReconstructConfigurationCommand, string>
    {
        private readonly IConfigurationPackageRepository _repository;
        private readonly IConfigurationDeltaEngine _deltaEngine;

        public ReconstructConfigurationCommandHandler(
            IConfigurationPackageRepository repository,
            IConfigurationDeltaEngine deltaEngine)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _deltaEngine = deltaEngine ?? throw new ArgumentNullException(nameof(deltaEngine));
        }

        public async Task<Result<string>> HandleAsync(ReconstructConfigurationCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                return Result.Failure<string>("NULL_COMMAND", "Command cannot be null.");
            }

            var name = string.IsNullOrWhiteSpace(command.Name) ? "default" : command.Name.Trim();

            var targetPackage = await _repository.GetByVersionNumberAsync(name, command.TargetVersionNumber, cancellationToken);
            if (targetPackage == null)
            {
                return Result.Failure<string>("VersionNotFound", $"Configuration version v{command.TargetVersionNumber} not found for scope '{name}'.");
            }

            if (targetPackage.PayloadType == ConfigurationPayloadType.Full)
            {
                return Result.Success(targetPackage.Content);
            }

            // Need to reconstruct from base
            var packages = await _repository.GetVersionRangeAsync(name, 1, command.TargetVersionNumber, cancellationToken);
            if (packages.Count == 0)
            {
                return Result.Failure<string>("VersionNotFound", "Version history not found.");
            }

            var map = packages.ToDictionary(p => p.VersionNumber);
            var current = targetPackage;
            var chain = new List<ConfigurationPackage> { current };

            while (current.PayloadType == ConfigurationPayloadType.Delta)
            {
                if (!current.BaseVersionNumber.HasValue)
                {
                    return Result.Failure<string>("InvalidBaseVersion", $"Delta v{current.VersionNumber} is missing BaseVersionNumber.");
                }

                if (!map.TryGetValue(current.BaseVersionNumber.Value, out var basePkg))
                {
                    return Result.Failure<string>("InvalidBaseVersion", $"Base version v{current.BaseVersionNumber.Value} not found in history.");
                }

                chain.Add(basePkg);
                current = basePkg;
            }

            // Chain is from Target down to Full. Reverse it so we apply from Full up to Target
            chain.Reverse();

            var rootFull = chain[0];
            string currentContent = rootFull.Content;

            for (int i = 1; i < chain.Count; i++)
            {
                var deltaPkg = chain[i];
                try
                {
                    currentContent = _deltaEngine.ApplyDelta(currentContent, deltaPkg.Content);
                }
                catch (Exception ex)
                {
                    return Result.Failure<string>("DeltaApplicationFailed", $"Failed to apply delta v{deltaPkg.VersionNumber} on base v{deltaPkg.BaseVersionNumber}: {ex.Message}");
                }
            }

            return Result.Success(currentContent);
        }
    }

    public record BuildDeltaChainCommand(
        string Name,
        long CurrentVersionNumber,
        long TargetVersionNumber,
        int MaxChainLength = 10) : ICommand<DeltaChainResult>;

    public class DeltaChainResult
    {
        public bool CanUseDeltaChain { get; set; }
        public List<ConfigurationPackage> Chain { get; set; } = new();
        public string FallbackReason { get; set; } = string.Empty;
    }

    public class BuildDeltaChainCommandHandler : ICommandHandler<BuildDeltaChainCommand, DeltaChainResult>
    {
        private readonly IConfigurationPackageRepository _repository;

        public BuildDeltaChainCommandHandler(IConfigurationPackageRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<Result<DeltaChainResult>> HandleAsync(BuildDeltaChainCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                return Result.Failure<DeltaChainResult>("NULL_COMMAND", "Command cannot be null.");
            }

            var name = string.IsNullOrWhiteSpace(command.Name) ? "default" : command.Name.Trim();

            if (command.CurrentVersionNumber >= command.TargetVersionNumber)
            {
                return Result.Success(new DeltaChainResult
                {
                    CanUseDeltaChain = false,
                    FallbackReason = "Current version is already at or beyond target version."
                });
            }

            var range = await _repository.GetVersionRangeAsync(name, command.CurrentVersionNumber + 1, command.TargetVersionNumber, cancellationToken);
            if (range.Count != (command.TargetVersionNumber - command.CurrentVersionNumber))
            {
                return Result.Success(new DeltaChainResult
                {
                    CanUseDeltaChain = false,
                    FallbackReason = "Incomplete version history available between current and target versions."
                });
            }

            if (range.Count > command.MaxChainLength)
            {
                return Result.Success(new DeltaChainResult
                {
                    CanUseDeltaChain = false,
                    FallbackReason = $"Delta chain length ({range.Count}) exceeds maximum allowed chain length of {command.MaxChainLength}."
                });
            }

            long expectedBase = command.CurrentVersionNumber;
            foreach (var pkg in range)
            {
                if (pkg.PayloadType != ConfigurationPayloadType.Delta)
                {
                    return Result.Success(new DeltaChainResult
                    {
                        CanUseDeltaChain = false,
                        FallbackReason = $"Package v{pkg.VersionNumber} in range is not a Delta package."
                    });
                }

                if (pkg.BaseVersionNumber != expectedBase)
                {
                    return Result.Success(new DeltaChainResult
                    {
                        CanUseDeltaChain = false,
                        FallbackReason = $"Mismatched base version for v{pkg.VersionNumber}. Expected base v{expectedBase}, got v{pkg.BaseVersionNumber}."
                    });
                }

                expectedBase = pkg.VersionNumber;
            }

            return Result.Success(new DeltaChainResult
            {
                CanUseDeltaChain = true,
                Chain = range
            });
        }
    }
}

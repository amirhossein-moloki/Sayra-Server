using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Configuration.Models;
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
}

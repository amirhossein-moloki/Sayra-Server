using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Sayra.Backend.Application.Configuration.Models;

namespace Sayra.Backend.Application.Configuration
{
    public class ConfigurationNormalizer : IConfigurationNormalizer
    {
        private readonly IConfigurationValidator _validator;

        public ConfigurationNormalizer(IConfigurationValidator validator)
        {
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        public ConfigurationNormalizer()
            : this(new ConfigurationValidatorService())
        {
        }

        public SayraConfigurationSchema NormalizeToModel(SayraConfigurationSchema schemaModel)
        {
            if (schemaModel == null)
            {
                throw new ArgumentNullException(nameof(schemaModel));
            }

            var validationResult = _validator.Validate(schemaModel);
            if (!validationResult.IsValid)
            {
                var errorMessages = string.Join("; ", validationResult.Errors);
                throw new InvalidOperationException($"Cannot normalize invalid configuration: {errorMessages}");
            }

            string normalizedCulture = schemaModel.Localization.Culture.Trim();
            try
            {
                var cultureInfo = CultureInfo.GetCultureInfo(normalizedCulture);
                if (!string.IsNullOrEmpty(cultureInfo.Name))
                {
                    normalizedCulture = cultureInfo.Name;
                }
            }
            catch
            {
                // Fallback to trimmed
            }

            return new SayraConfigurationSchema
            {
                Version = schemaModel.Version.Trim(),
                Server = new ServerConfigurationSection
                {
                    IpAddress = schemaModel.Server.IpAddress.Trim(),
                    Port = schemaModel.Server.Port
                },
                Discovery = new DiscoveryConfigurationSection
                {
                    Enabled = schemaModel.Discovery.Enabled,
                    Port = schemaModel.Discovery.Port
                },
                Heartbeat = new HeartbeatConfigurationSection
                {
                    IntervalSeconds = schemaModel.Heartbeat.IntervalSeconds,
                    TimeoutSeconds = schemaModel.Heartbeat.TimeoutSeconds
                },
                Kiosk = new KioskConfigurationSection
                {
                    Enabled = schemaModel.Kiosk.Enabled,
                    AllowShellEscape = schemaModel.Kiosk.AllowShellEscape,
                    AutoLoginGamer = schemaModel.Kiosk.AutoLoginGamer,
                    IdleTimeoutMinutes = schemaModel.Kiosk.IdleTimeoutMinutes
                },
                Localization = new LocalizationConfigurationSection
                {
                    Culture = normalizedCulture,
                    TimeZone = schemaModel.Localization.TimeZone.Trim()
                },
                Security = new SecurityConfigurationSection
                {
                    EnableSsl = schemaModel.Security.EnableSsl,
                    RequireEncryption = schemaModel.Security.RequireEncryption,
                    MaxFailedAttempts = schemaModel.Security.MaxFailedAttempts
                }
            };
        }

        public string NormalizeToJson(SayraConfigurationSchema schemaModel)
        {
            var normalized = NormalizeToModel(schemaModel);

            // Construct sorted dictionary tree (Ordinal key comparison) for deterministic canonical JSON representation
            var canonicalMap = new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["version"] = normalized.Version,

                ["server"] = new SortedDictionary<string, object>(StringComparer.Ordinal)
                {
                    ["ipAddress"] = normalized.Server.IpAddress,
                    ["port"] = normalized.Server.Port
                },

                ["discovery"] = new SortedDictionary<string, object>(StringComparer.Ordinal)
                {
                    ["enabled"] = normalized.Discovery.Enabled,
                    ["port"] = normalized.Discovery.Port
                },

                ["heartbeat"] = new SortedDictionary<string, object>(StringComparer.Ordinal)
                {
                    ["intervalSeconds"] = normalized.Heartbeat.IntervalSeconds,
                    ["timeoutSeconds"] = normalized.Heartbeat.TimeoutSeconds
                },

                ["kiosk"] = new SortedDictionary<string, object>(StringComparer.Ordinal)
                {
                    ["allowShellEscape"] = normalized.Kiosk.AllowShellEscape,
                    ["autoLoginGamer"] = normalized.Kiosk.AutoLoginGamer,
                    ["enabled"] = normalized.Kiosk.Enabled,
                    ["idleTimeoutMinutes"] = normalized.Kiosk.IdleTimeoutMinutes
                },

                ["localization"] = new SortedDictionary<string, object>(StringComparer.Ordinal)
                {
                    ["culture"] = normalized.Localization.Culture,
                    ["timeZone"] = normalized.Localization.TimeZone
                },

                ["security"] = new SortedDictionary<string, object>(StringComparer.Ordinal)
                {
                    ["enableSsl"] = normalized.Security.EnableSsl,
                    ["maxFailedAttempts"] = normalized.Security.MaxFailedAttempts,
                    ["requireEncryption"] = normalized.Security.RequireEncryption
                }
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = false
            };

            return JsonSerializer.Serialize(canonicalMap, options);
        }

        public string NormalizeToJson(string rawJsonPayload)
        {
            if (string.IsNullOrWhiteSpace(rawJsonPayload))
            {
                throw new ArgumentException("Configuration payload cannot be null or empty.", nameof(rawJsonPayload));
            }

            var validationResult = _validator.Validate(rawJsonPayload);
            if (!validationResult.IsValid)
            {
                var errors = new List<string>();
                foreach (var err in validationResult.Errors)
                {
                    errors.Add($"[{err.Path}] {err.Code}: {err.Message}");
                }
                throw new InvalidOperationException($"Cannot normalize invalid configuration: {string.Join("; ", errors)}");
            }

            var schemaModel = JsonSerializer.Deserialize<SayraConfigurationSchema>(rawJsonPayload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (schemaModel == null)
            {
                throw new InvalidOperationException("Failed to deserialize configuration payload.");
            }

            return NormalizeToJson(schemaModel);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sayra.Backend.Application.Configuration.Models;

namespace Sayra.Backend.Application.Configuration
{
    public class ConfigurationValidatorService : IConfigurationValidator
    {
        private static readonly HashSet<string> KnownRootProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "version", "server", "discovery", "heartbeat", "kiosk", "localization", "security"
        };

        private static readonly HashSet<string> KnownServerProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ipAddress", "port"
        };

        private static readonly HashSet<string> KnownDiscoveryProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "enabled", "port"
        };

        private static readonly HashSet<string> KnownHeartbeatProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "intervalSeconds", "timeoutSeconds"
        };

        private static readonly HashSet<string> KnownKioskProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "enabled", "allowShellEscape", "autoLoginGamer", "idleTimeoutMinutes"
        };

        private static readonly HashSet<string> KnownLocalizationProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "culture", "timeZone"
        };

        private static readonly HashSet<string> KnownSecurityProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "enableSsl", "requireEncryption", "maxFailedAttempts"
        };

        private static readonly Regex HostnameRegex = new Regex(
            @"^(?:[a-zA-Z0-9](?:[a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,6}$|^localhost$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public ConfigurationValidationResult Validate(string rawJsonPayload)
        {
            var result = new ConfigurationValidationResult();

            if (string.IsNullOrWhiteSpace(rawJsonPayload))
            {
                result.AddError("", "PAYLOAD_EMPTY", "Configuration payload cannot be null, empty, or whitespace.");
                return result;
            }

            var utf8ByteCount = Encoding.UTF8.GetByteCount(rawJsonPayload);
            if (utf8ByteCount > ConfigurationLimits.MaxPayloadSizeBytes)
            {
                result.AddError("", "EXCEEDS_MAX_SIZE", $"Configuration payload size ({utf8ByteCount} bytes) exceeds maximum limit of {ConfigurationLimits.MaxPayloadSizeBytes} bytes.");
                return result;
            }

            JsonDocument doc;
            try
            {
                var options = new JsonDocumentOptions
                {
                    MaxDepth = ConfigurationLimits.MaxNestingDepth,
                    CommentHandling = JsonCommentHandling.Disallow
                };
                doc = JsonDocument.Parse(rawJsonPayload, options);
            }
            catch (JsonException ex)
            {
                if (ex.Message.Contains("depth", StringComparison.OrdinalIgnoreCase) &&
                    (ex.Message.Contains("exceed", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("max", StringComparison.OrdinalIgnoreCase)))
                {
                    result.AddError("", "EXCEEDS_MAX_DEPTH", $"Configuration payload exceeds maximum allowed depth of {ConfigurationLimits.MaxNestingDepth}.");
                }
                else
                {
                    result.AddError("", "INVALID_JSON", "Configuration payload is not valid JSON syntax.");
                }
                return result;
            }

            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    result.AddError("", "NON_OBJECT_ROOT", "Root configuration element must be a JSON object.");
                    return result;
                }

                // Measure depth explicitly to be 100% thorough
                var currentDepth = GetElementDepth(doc.RootElement, 1);
                if (currentDepth > ConfigurationLimits.MaxNestingDepth)
                {
                    result.AddError("", "EXCEEDS_MAX_DEPTH", $"Configuration payload depth ({currentDepth}) exceeds maximum allowed depth of {ConfigurationLimits.MaxNestingDepth}.");
                    return result;
                }

                // Check structural unknown properties & value types
                InspectJsonStructure(doc.RootElement, "", result);

                if (!result.IsValid)
                {
                    return result;
                }

                // Deserialize and run semantic validation
                SayraConfigurationSchema? schemaModel = null;
                try
                {
                    schemaModel = JsonSerializer.Deserialize<SayraConfigurationSchema>(rawJsonPayload, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (Exception)
                {
                    result.AddError("", "DESERIALIZATION_FAILED", "Failed to deserialize configuration payload into schema model.");
                    return result;
                }

                if (schemaModel == null)
                {
                    result.AddError("", "NULL_SCHEMA", "Deserialized configuration schema model is null.");
                    return result;
                }

                var semanticResult = Validate(schemaModel);
                foreach (var err in semanticResult.Errors)
                {
                    result.Errors.Add(err);
                }
            }

            return result;
        }

        public ConfigurationValidationResult Validate(SayraConfigurationSchema schemaModel)
        {
            var result = new ConfigurationValidationResult();

            if (schemaModel == null)
            {
                result.AddError("", "NULL_SCHEMA", "Configuration schema model cannot be null.");
                return result;
            }

            // Version
            if (string.IsNullOrWhiteSpace(schemaModel.Version))
            {
                result.AddError("version", "REQUIRED_FIELD_MISSING", "Configuration version cannot be null or empty.");
            }
            else if (schemaModel.Version.Length > ConfigurationLimits.MaxStringLength)
            {
                result.AddError("version", "EXCEEDS_MAX_STRING_LENGTH", $"Version string length exceeds maximum of {ConfigurationLimits.MaxStringLength}.");
            }

            // Server
            if (schemaModel.Server == null)
            {
                result.AddError("server", "REQUIRED_FIELD_MISSING", "Server configuration section is required.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(schemaModel.Server.IpAddress))
                {
                    result.AddError("server.ipAddress", "REQUIRED_FIELD_MISSING", "Server IP address or host is required.");
                }
                else if (schemaModel.Server.IpAddress.Length > ConfigurationLimits.MaxStringLength)
                {
                    result.AddError("server.ipAddress", "EXCEEDS_MAX_STRING_LENGTH", $"Server IP address string length exceeds maximum of {ConfigurationLimits.MaxStringLength}.");
                }
                else if (!IsValidIpOrHost(schemaModel.Server.IpAddress))
                {
                    result.AddError("server.ipAddress", "INVALID_FORMAT", $"Server IP address or hostname '{schemaModel.Server.IpAddress}' is invalid.");
                }

                if (schemaModel.Server.Port <= 0 || schemaModel.Server.Port > 65535)
                {
                    result.AddError("server.port", "OUT_OF_RANGE", $"Server port {schemaModel.Server.Port} is out of valid range (1-65535).");
                }
            }

            // Discovery
            if (schemaModel.Discovery == null)
            {
                result.AddError("discovery", "REQUIRED_FIELD_MISSING", "Discovery configuration section is required.");
            }
            else
            {
                if (schemaModel.Discovery.Port <= 0 || schemaModel.Discovery.Port > 65535)
                {
                    result.AddError("discovery.port", "OUT_OF_RANGE", $"Discovery port {schemaModel.Discovery.Port} is out of valid range (1-65535).");
                }
            }

            // Heartbeat
            if (schemaModel.Heartbeat == null)
            {
                result.AddError("heartbeat", "REQUIRED_FIELD_MISSING", "Heartbeat configuration section is required.");
            }
            else
            {
                if (schemaModel.Heartbeat.IntervalSeconds <= 0)
                {
                    result.AddError("heartbeat.intervalSeconds", "OUT_OF_RANGE", $"Heartbeat intervalSeconds ({schemaModel.Heartbeat.IntervalSeconds}) must be greater than 0.");
                }

                if (schemaModel.Heartbeat.TimeoutSeconds <= 0)
                {
                    result.AddError("heartbeat.timeoutSeconds", "OUT_OF_RANGE", $"Heartbeat timeoutSeconds ({schemaModel.Heartbeat.TimeoutSeconds}) must be greater than 0.");
                }

                if (schemaModel.Heartbeat.IntervalSeconds > 0 &&
                    schemaModel.Heartbeat.TimeoutSeconds > 0 &&
                    schemaModel.Heartbeat.TimeoutSeconds <= schemaModel.Heartbeat.IntervalSeconds)
                {
                    result.AddError("heartbeat.timeoutSeconds", "INVALID_SEMANTICS", $"Heartbeat timeoutSeconds ({schemaModel.Heartbeat.TimeoutSeconds}) must be strictly greater than intervalSeconds ({schemaModel.Heartbeat.IntervalSeconds}).");
                }
            }

            // Kiosk
            if (schemaModel.Kiosk == null)
            {
                result.AddError("kiosk", "REQUIRED_FIELD_MISSING", "Kiosk configuration section is required.");
            }
            else
            {
                if (schemaModel.Kiosk.IdleTimeoutMinutes < 0)
                {
                    result.AddError("kiosk.idleTimeoutMinutes", "OUT_OF_RANGE", $"Kiosk idleTimeoutMinutes ({schemaModel.Kiosk.IdleTimeoutMinutes}) cannot be negative.");
                }
            }

            // Localization
            if (schemaModel.Localization == null)
            {
                result.AddError("localization", "REQUIRED_FIELD_MISSING", "Localization configuration section is required.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(schemaModel.Localization.Culture))
                {
                    result.AddError("localization.culture", "REQUIRED_FIELD_MISSING", "Localization culture is required.");
                }
                else if (schemaModel.Localization.Culture.Length > ConfigurationLimits.MaxStringLength)
                {
                    result.AddError("localization.culture", "EXCEEDS_MAX_STRING_LENGTH", $"Culture string length exceeds maximum of {ConfigurationLimits.MaxStringLength}.");
                }
                else if (!IsValidCulture(schemaModel.Localization.Culture))
                {
                    result.AddError("localization.culture", "INVALID_CULTURE", $"Culture '{schemaModel.Localization.Culture}' is not a recognized or valid culture identifier.");
                }

                if (string.IsNullOrWhiteSpace(schemaModel.Localization.TimeZone))
                {
                    result.AddError("localization.timeZone", "REQUIRED_FIELD_MISSING", "Localization timeZone is required.");
                }
                else if (schemaModel.Localization.TimeZone.Length > ConfigurationLimits.MaxStringLength)
                {
                    result.AddError("localization.timeZone", "EXCEEDS_MAX_STRING_LENGTH", $"TimeZone string length exceeds maximum of {ConfigurationLimits.MaxStringLength}.");
                }
                else if (!IsValidTimeZone(schemaModel.Localization.TimeZone))
                {
                    result.AddError("localization.timeZone", "INVALID_TIMEZONE", $"TimeZone '{schemaModel.Localization.TimeZone}' is not a recognized or valid time zone identifier.");
                }
            }

            // Security
            if (schemaModel.Security == null)
            {
                result.AddError("security", "REQUIRED_FIELD_MISSING", "Security configuration section is required.");
            }
            else
            {
                if (schemaModel.Security.MaxFailedAttempts <= 0 || schemaModel.Security.MaxFailedAttempts > 100)
                {
                    result.AddError("security.maxFailedAttempts", "OUT_OF_RANGE", $"Security maxFailedAttempts ({schemaModel.Security.MaxFailedAttempts}) must be between 1 and 100.");
                }
            }

            return result;
        }

        private static int GetElementDepth(JsonElement element, int currentDepth)
        {
            if (currentDepth > ConfigurationLimits.MaxNestingDepth)
            {
                return currentDepth;
            }

            int maxDepth = currentDepth;

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    var depth = GetElementDepth(prop.Value, currentDepth + 1);
                    if (depth > maxDepth)
                    {
                        maxDepth = depth;
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    var depth = GetElementDepth(item, currentDepth + 1);
                    if (depth > maxDepth)
                    {
                        maxDepth = depth;
                    }
                }
            }

            return maxDepth;
        }

        private static void InspectJsonStructure(JsonElement root, string parentPath, ConfigurationValidationResult result)
        {
            foreach (var prop in root.EnumerateObject())
            {
                var propName = prop.Name;
                var currentPath = string.IsNullOrEmpty(parentPath) ? propName : $"{parentPath}.{propName}";

                if (string.IsNullOrEmpty(parentPath))
                {
                    if (!KnownRootProperties.Contains(propName))
                    {
                        result.AddError(currentPath, "UNKNOWN_PROPERTY", $"Root property '{propName}' is not supported in configuration schema.");
                        continue;
                    }
                }
                else if (parentPath.Equals("server", StringComparison.OrdinalIgnoreCase))
                {
                    if (!KnownServerProperties.Contains(propName))
                    {
                        result.AddError(currentPath, "UNKNOWN_PROPERTY", $"Server property '{propName}' is not supported in configuration schema.");
                        continue;
                    }
                }
                else if (parentPath.Equals("discovery", StringComparison.OrdinalIgnoreCase))
                {
                    if (!KnownDiscoveryProperties.Contains(propName))
                    {
                        result.AddError(currentPath, "UNKNOWN_PROPERTY", $"Discovery property '{propName}' is not supported in configuration schema.");
                        continue;
                    }
                }
                else if (parentPath.Equals("heartbeat", StringComparison.OrdinalIgnoreCase))
                {
                    if (!KnownHeartbeatProperties.Contains(propName))
                    {
                        result.AddError(currentPath, "UNKNOWN_PROPERTY", $"Heartbeat property '{propName}' is not supported in configuration schema.");
                        continue;
                    }
                }
                else if (parentPath.Equals("kiosk", StringComparison.OrdinalIgnoreCase))
                {
                    if (!KnownKioskProperties.Contains(propName))
                    {
                        result.AddError(currentPath, "UNKNOWN_PROPERTY", $"Kiosk property '{propName}' is not supported in configuration schema.");
                        continue;
                    }
                }
                else if (parentPath.Equals("localization", StringComparison.OrdinalIgnoreCase))
                {
                    if (!KnownLocalizationProperties.Contains(propName))
                    {
                        result.AddError(currentPath, "UNKNOWN_PROPERTY", $"Localization property '{propName}' is not supported in configuration schema.");
                        continue;
                    }
                }
                else if (parentPath.Equals("security", StringComparison.OrdinalIgnoreCase))
                {
                    if (!KnownSecurityProperties.Contains(propName))
                    {
                        result.AddError(currentPath, "UNKNOWN_PROPERTY", $"Security property '{propName}' is not supported in configuration schema.");
                        continue;
                    }
                }

                // Inspect property string lengths
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var val = prop.Value.GetString();
                    if (val != null && val.Length > ConfigurationLimits.MaxStringLength)
                    {
                        result.AddError(currentPath, "EXCEEDS_MAX_STRING_LENGTH", $"Property '{currentPath}' length ({val.Length}) exceeds maximum of {ConfigurationLimits.MaxStringLength}.");
                    }
                }
                else if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    InspectJsonStructure(prop.Value, currentPath, result);
                }
            }
        }

        private static bool IsValidIpOrHost(string input)
        {
            if (IPAddress.TryParse(input, out _))
            {
                return true;
            }

            if (input.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return HostnameRegex.IsMatch(input);
        }

        private static bool IsValidCulture(string cultureName)
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(cultureName);
                return culture != null && !string.IsNullOrEmpty(culture.Name);
            }
            catch (CultureNotFoundException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsValidTimeZone(string timeZoneId)
        {
            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return true;
            }
            catch (TimeZoneNotFoundException)
            {
                return false;
            }
            catch (InvalidTimeZoneException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}

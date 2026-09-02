using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Sayra.Backend.Application.Configuration.Models;

#nullable enable

namespace Sayra.Backend.Application.Configuration
{
    /// <summary>
    /// Production implementation of deterministic canonical configuration serializer.
    /// Sorts all JSON object keys recursively using StringComparer.Ordinal, preserves
    /// semantic array element ordering, and outputs compact UTF-8 JSON bytes.
    /// </summary>
    public class CanonicalConfigurationSerializer : ICanonicalConfigurationSerializer
    {
        private static readonly JsonSerializerOptions CanonicalOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = true
        };

        public string SerializeToCanonicalJson(object modelOrPayload)
        {
            if (modelOrPayload == null)
            {
                throw new ArgumentNullException(nameof(modelOrPayload));
            }

            if (modelOrPayload is string jsonString)
            {
                return SerializeToCanonicalJson(jsonString);
            }

            if (modelOrPayload is SayraConfigurationSchema schema)
            {
                var dict = ConvertSchemaToSortedMap(schema);
                return JsonSerializer.Serialize(dict, CanonicalOptions);
            }

            // Convert general object to JsonDocument and then process recursively
            string raw = JsonSerializer.Serialize(modelOrPayload, CanonicalOptions);
            return SerializeToCanonicalJson(raw);
        }

        public string SerializeToCanonicalJson(string rawJsonPayload)
        {
            if (string.IsNullOrWhiteSpace(rawJsonPayload))
            {
                throw new ArgumentException("Configuration payload cannot be null or empty.", nameof(rawJsonPayload));
            }

            using var doc = JsonDocument.Parse(rawJsonPayload);
            var canonicalObject = NormalizeElement(doc.RootElement);

            return JsonSerializer.Serialize(canonicalObject, CanonicalOptions);
        }

        public byte[] SerializeToCanonicalBytes(object modelOrPayload)
        {
            string json = SerializeToCanonicalJson(modelOrPayload);
            return Encoding.UTF8.GetBytes(json);
        }

        public byte[] SerializeToCanonicalBytes(string rawJsonPayload)
        {
            string json = SerializeToCanonicalJson(rawJsonPayload);
            return Encoding.UTF8.GetBytes(json);
        }

        private static object? NormalizeElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var sortedMap = new SortedDictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var prop in element.EnumerateObject())
                    {
                        sortedMap[prop.Name] = NormalizeElement(prop.Value);
                    }
                    return sortedMap;

                case JsonValueKind.Array:
                    var list = new List<object?>();
                    foreach (var item in element.EnumerateArray())
                    {
                        list.Add(NormalizeElement(item));
                    }
                    return list;

                case JsonValueKind.String:
                    return element.GetString();

                case JsonValueKind.Number:
                    if (element.TryGetInt64(out long longValue))
                    {
                        return longValue;
                    }
                    if (element.TryGetDecimal(out decimal decimalValue))
                    {
                        return decimalValue;
                    }
                    return element.GetDouble();

                case JsonValueKind.True:
                    return true;

                case JsonValueKind.False:
                    return false;

                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                default:
                    return null;
            }
        }

        private static SortedDictionary<string, object?> ConvertSchemaToSortedMap(SayraConfigurationSchema schema)
        {
            return new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["version"] = schema.Version?.Trim() ?? string.Empty,

                ["discovery"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["enabled"] = schema.Discovery.Enabled,
                    ["port"] = schema.Discovery.Port
                },

                ["heartbeat"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["intervalSeconds"] = schema.Heartbeat.IntervalSeconds,
                    ["timeoutSeconds"] = schema.Heartbeat.TimeoutSeconds
                },

                ["kiosk"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["allowShellEscape"] = schema.Kiosk.AllowShellEscape,
                    ["autoLoginGamer"] = schema.Kiosk.AutoLoginGamer,
                    ["enabled"] = schema.Kiosk.Enabled,
                    ["idleTimeoutMinutes"] = schema.Kiosk.IdleTimeoutMinutes
                },

                ["localization"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["culture"] = schema.Localization.Culture?.Trim() ?? string.Empty,
                    ["timeZone"] = schema.Localization.TimeZone?.Trim() ?? string.Empty
                },

                ["security"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["enableSsl"] = schema.Security.EnableSsl,
                    ["maxFailedAttempts"] = schema.Security.MaxFailedAttempts,
                    ["requireEncryption"] = schema.Security.RequireEncryption
                },

                ["server"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["ipAddress"] = schema.Server.IpAddress?.Trim() ?? string.Empty,
                    ["port"] = schema.Server.Port
                }
            };
        }
    }
}

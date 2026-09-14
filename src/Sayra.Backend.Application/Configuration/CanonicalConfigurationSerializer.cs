using System;
using System.Buffers;
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
    /// semantic array element ordering, and streams compact UTF-8 JSON bytes with
    /// zero/low allocations.
    /// </summary>
    public class CanonicalConfigurationSerializer : ICanonicalConfigurationSerializer
    {
        private static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions
        {
            Indented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static readonly JsonSerializerOptions GeneralOptions = new JsonSerializerOptions
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
                byte[] bytes = SerializeSchemaToCanonicalBytes(schema);
                return Encoding.UTF8.GetString(bytes);
            }

            // Convert general object to JsonDocument and then process recursively
            string raw = JsonSerializer.Serialize(modelOrPayload, GeneralOptions);
            return SerializeToCanonicalJson(raw);
        }

        public string SerializeToCanonicalJson(string rawJsonPayload)
        {
            byte[] bytes = SerializeToCanonicalBytes(rawJsonPayload);
            return Encoding.UTF8.GetString(bytes);
        }

        public byte[] SerializeToCanonicalBytes(object modelOrPayload)
        {
            if (modelOrPayload == null)
            {
                throw new ArgumentNullException(nameof(modelOrPayload));
            }

            if (modelOrPayload is string jsonString)
            {
                return SerializeToCanonicalBytes(jsonString);
            }

            if (modelOrPayload is SayraConfigurationSchema schema)
            {
                return SerializeSchemaToCanonicalBytes(schema);
            }

            string raw = JsonSerializer.Serialize(modelOrPayload, GeneralOptions);
            return SerializeToCanonicalBytes(raw);
        }

        public byte[] SerializeToCanonicalBytes(string rawJsonPayload)
        {
            if (string.IsNullOrWhiteSpace(rawJsonPayload))
            {
                throw new ArgumentException("Configuration payload cannot be null or empty.", nameof(rawJsonPayload));
            }

            using var doc = JsonDocument.Parse(rawJsonPayload);
            var bufferWriter = new ArrayBufferWriter<byte>(1024);
            using (var writer = new Utf8JsonWriter(bufferWriter, WriterOptions))
            {
                WriteCanonicalElement(doc.RootElement, writer);
            }

            return bufferWriter.WrittenSpan.ToArray();
        }

        private static void WriteCanonicalElement(JsonElement element, Utf8JsonWriter writer)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    var properties = new List<JsonProperty>(8);
                    foreach (var prop in element.EnumerateObject())
                    {
                        properties.Add(prop);
                    }

                    if (properties.Count > 0)
                    {
                        properties.Sort(JsonPropertyNameComparer.Instance);
                        for (int i = 0; i < properties.Count; i++)
                        {
                            writer.WritePropertyName(properties[i].Name);
                            WriteCanonicalElement(properties[i].Value, writer);
                        }
                    }
                    writer.WriteEndObject();
                    break;

                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray())
                    {
                        WriteCanonicalElement(item, writer);
                    }
                    writer.WriteEndArray();
                    break;

                case JsonValueKind.String:
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                default:
                    element.WriteTo(writer);
                    break;
            }
        }

        private static byte[] SerializeSchemaToCanonicalBytes(SayraConfigurationSchema schema)
        {
            var bufferWriter = new ArrayBufferWriter<byte>(512);
            using (var writer = new Utf8JsonWriter(bufferWriter, WriterOptions))
            {
                writer.WriteStartObject();

                // 1. discovery
                writer.WriteStartObject("discovery");
                writer.WriteBoolean("enabled", schema.Discovery.Enabled);
                writer.WriteNumber("port", schema.Discovery.Port);
                writer.WriteEndObject();

                // 2. heartbeat
                writer.WriteStartObject("heartbeat");
                writer.WriteNumber("intervalSeconds", schema.Heartbeat.IntervalSeconds);
                writer.WriteNumber("timeoutSeconds", schema.Heartbeat.TimeoutSeconds);
                writer.WriteEndObject();

                // 3. kiosk
                writer.WriteStartObject("kiosk");
                writer.WriteBoolean("allowShellEscape", schema.Kiosk.AllowShellEscape);
                writer.WriteBoolean("autoLoginGamer", schema.Kiosk.AutoLoginGamer);
                writer.WriteBoolean("enabled", schema.Kiosk.Enabled);
                writer.WriteNumber("idleTimeoutMinutes", schema.Kiosk.IdleTimeoutMinutes);
                writer.WriteEndObject();

                // 4. localization
                writer.WriteStartObject("localization");
                writer.WriteString("culture", schema.Localization.Culture?.Trim() ?? string.Empty);
                writer.WriteString("timeZone", schema.Localization.TimeZone?.Trim() ?? string.Empty);
                writer.WriteEndObject();

                // 5. security
                writer.WriteStartObject("security");
                writer.WriteBoolean("enableSsl", schema.Security.EnableSsl);
                writer.WriteNumber("maxFailedAttempts", schema.Security.MaxFailedAttempts);
                writer.WriteBoolean("requireEncryption", schema.Security.RequireEncryption);
                writer.WriteEndObject();

                // 6. server
                writer.WriteStartObject("server");
                writer.WriteString("ipAddress", schema.Server.IpAddress?.Trim() ?? string.Empty);
                writer.WriteNumber("port", schema.Server.Port);
                writer.WriteEndObject();

                // 7. version
                writer.WriteString("version", schema.Version?.Trim() ?? string.Empty);

                writer.WriteEndObject();
            }

            return bufferWriter.WrittenSpan.ToArray();
        }

        private sealed class JsonPropertyNameComparer : IComparer<JsonProperty>
        {
            public static readonly JsonPropertyNameComparer Instance = new JsonPropertyNameComparer();

            public int Compare(JsonProperty x, JsonProperty y)
            {
                return string.Compare(x.Name, y.Name, StringComparison.Ordinal);
            }
        }
    }
}

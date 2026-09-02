using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sayra.Backend.Application.Configuration.Models;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Application.Configuration
{
    public class ConfigurationDeltaEngine : IConfigurationDeltaEngine
    {
        private static readonly HashSet<string> AllowedTopLevelSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "server", "discovery", "heartbeat", "kiosk", "localization", "security"
        };

        private readonly IConfigurationNormalizer _normalizer;
        private readonly IConfigurationValidator _validator;

        public ConfigurationDeltaEngine(IConfigurationNormalizer normalizer, IConfigurationValidator validator)
        {
            _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        public ConfigurationDeltaEngine()
            : this(new ConfigurationNormalizer(), new ConfigurationValidatorService())
        {
        }

        public string ApplyDelta(string baseNormalizedJson, IEnumerable<ConfigurationDelta> deltas)
        {
            if (string.IsNullOrWhiteSpace(baseNormalizedJson))
            {
                throw new ArgumentException("Base normalized configuration JSON cannot be null or empty.", nameof(baseNormalizedJson));
            }

            if (deltas == null)
            {
                throw new ArgumentNullException(nameof(deltas));
            }

            var deltaList = deltas.ToList();

            // Validate base config first
            var baseValidation = _validator.Validate(baseNormalizedJson);
            if (!baseValidation.IsValid)
            {
                var errors = string.Join("; ", baseValidation.Errors.Select(e => $"[{e.Path}] {e.Code}: {e.Message}"));
                throw new InvalidOperationException($"Cannot apply delta to invalid base configuration: {errors}");
            }

            JsonNode rootNode;
            try
            {
                rootNode = JsonNode.Parse(baseNormalizedJson) ?? new JsonObject();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse base configuration JSON: {ex.Message}", ex);
            }

            foreach (var delta in deltaList)
            {
                ApplySingleDelta(rootNode, delta);
            }

            string targetRawJson = rootNode.ToJsonString();

            // Apply-then-Normalize-then-Validate pipeline
            string targetNormalizedJson = _normalizer.NormalizeToJson(targetRawJson);
            var targetValidation = _validator.Validate(targetNormalizedJson);
            if (!targetValidation.IsValid)
            {
                var errors = string.Join("; ", targetValidation.Errors.Select(e => $"[{e.Path}] {e.Code}: {e.Message}"));
                throw new InvalidOperationException($"Delta application resulted in an invalid configuration: {errors}");
            }

            return targetNormalizedJson;
        }

        public string ApplyDelta(string baseNormalizedJson, string deltaJson)
        {
            if (string.IsNullOrWhiteSpace(deltaJson))
            {
                throw new ArgumentException("Delta JSON payload cannot be null or empty.", nameof(deltaJson));
            }

            List<ConfigurationDelta>? deltas;
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                deltas = JsonSerializer.Deserialize<List<ConfigurationDelta>>(deltaJson, options);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to deserialize delta payload: {ex.Message}", ex);
            }

            if (deltas == null)
            {
                throw new InvalidOperationException("Deserialized delta payload is null.");
            }

            return ApplyDelta(baseNormalizedJson, deltas);
        }

        public List<ConfigurationDelta> ComputeDelta(string baseNormalizedJson, string targetNormalizedJson)
        {
            if (string.IsNullOrWhiteSpace(baseNormalizedJson))
            {
                throw new ArgumentException("Base configuration JSON cannot be null or empty.", nameof(baseNormalizedJson));
            }

            if (string.IsNullOrWhiteSpace(targetNormalizedJson))
            {
                throw new ArgumentException("Target configuration JSON cannot be null or empty.", nameof(targetNormalizedJson));
            }

            // Normalize both inputs to ensure structural consistency
            var normBase = _normalizer.NormalizeToJson(baseNormalizedJson);
            var normTarget = _normalizer.NormalizeToJson(targetNormalizedJson);

            using var baseDoc = JsonDocument.Parse(normBase);
            using var targetDoc = JsonDocument.Parse(normTarget);

            var deltas = new List<ConfigurationDelta>();
            CompareJsonElements(baseDoc.RootElement, targetDoc.RootElement, "", deltas);
            return deltas;
        }

        private void ApplySingleDelta(JsonNode rootNode, ConfigurationDelta delta)
        {
            if (delta == null)
            {
                throw new InvalidOperationException("Delta operation cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(delta.Path))
            {
                throw new InvalidOperationException("Delta operation path cannot be null or empty.");
            }

            var op = delta.Op?.ToLowerInvariant();
            if (op != "add" && op != "replace" && op != "remove")
            {
                throw new InvalidOperationException($"Unsupported delta operation: '{delta.Op}'. Supported operations are 'add', 'replace', 'remove'.");
            }

            // Path Safety Check
            ValidatePathSafety(delta.Path);

            var segments = ParseJsonPointerPath(delta.Path);
            if (segments.Count == 0)
            {
                throw new InvalidOperationException("Delta path cannot target root element directly.");
            }

            // Traverse to parent node
            JsonNode current = rootNode;
            for (int i = 0; i < segments.Count - 1; i++)
            {
                var seg = segments[i];
                if (current is JsonObject obj)
                {
                    if (!obj.ContainsKey(seg) || obj[seg] == null)
                    {
                        throw new InvalidOperationException($"Path segment '{seg}' not found in configuration.");
                    }
                    current = obj[seg]!;
                }
                else
                {
                    throw new InvalidOperationException($"Path segment '{seg}' is not a JSON object.");
                }
            }

            var lastSegment = segments[segments.Count - 1];
            if (current is not JsonObject parentObject)
            {
                throw new InvalidOperationException($"Parent node for property '{lastSegment}' is not a JSON object.");
            }

            if (op == "remove")
            {
                if (!parentObject.ContainsKey(lastSegment))
                {
                    throw new InvalidOperationException($"Cannot remove non-existent path '{delta.Path}'.");
                }
                parentObject.Remove(lastSegment);
            }
            else if (op == "add" || op == "replace")
            {
                if (op == "replace" && !parentObject.ContainsKey(lastSegment))
                {
                    throw new InvalidOperationException($"Cannot replace non-existent path '{delta.Path}'.");
                }

                JsonNode? valueNode = ConvertToNode(delta.Value);
                parentObject[lastSegment] = valueNode;
            }
        }

        private static void ValidatePathSafety(string path)
        {
            var segments = ParseJsonPointerPath(path);
            if (segments.Count == 0)
            {
                throw new InvalidOperationException("Invalid delta path.");
            }

            var rootProp = segments[0];

            if (rootProp.Equals("version", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Modifying configuration version metadata via delta is strictly forbidden.");
            }

            if (!AllowedTopLevelSections.Contains(rootProp))
            {
                throw new InvalidOperationException($"Delta path '{path}' targets forbidden or unknown top-level section '{rootProp}'.");
            }
        }

        private static List<string> ParseJsonPointerPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new List<string>();
            }

            var trimmed = path.Trim();
            if (trimmed.StartsWith("/"))
            {
                trimmed = trimmed.Substring(1);
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return new List<string>();
            }

            return trimmed.Split('/')
                          .Select(s => s.Replace("~1", "/").Replace("~0", "~"))
                          .ToList();
        }

        private static JsonNode? ConvertToNode(object? value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is JsonElement elem)
            {
                return JsonNode.Parse(elem.GetRawText());
            }

            var jsonString = JsonSerializer.Serialize(value);
            return JsonNode.Parse(jsonString);
        }

        private static void CompareJsonElements(JsonElement baseElem, JsonElement targetElem, string currentPath, List<ConfigurationDelta> deltas)
        {
            if (baseElem.ValueKind == JsonValueKind.Object && targetElem.ValueKind == JsonValueKind.Object)
            {
                var baseProps = baseElem.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var targetProps = targetElem.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

                // Removed properties
                foreach (var bp in baseProps)
                {
                    if (!targetProps.ContainsKey(bp.Key))
                    {
                        var path = $"{currentPath}/{bp.Key}";
                        deltas.Add(new ConfigurationDelta { Op = "remove", Path = path, Value = null });
                    }
                }

                // Added or replaced properties
                foreach (var tp in targetProps)
                {
                    var path = $"{currentPath}/{tp.Key}";
                    if (!baseProps.ContainsKey(tp.Key))
                    {
                        deltas.Add(new ConfigurationDelta { Op = "add", Path = path, Value = GetElementValue(tp.Value) });
                    }
                    else
                    {
                        CompareJsonElements(baseProps[tp.Key], tp.Value, path, deltas);
                    }
                }
            }
            else
            {
                if (!JsonElementEquals(baseElem, targetElem))
                {
                    deltas.Add(new ConfigurationDelta { Op = "replace", Path = currentPath, Value = GetElementValue(targetElem) });
                }
            }
        }

        private static object? GetElementValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => JsonDocument.Parse(element.GetRawText()).RootElement.Clone()
            };
        }

        private static bool JsonElementEquals(JsonElement elem1, JsonElement elem2)
        {
            return elem1.GetRawText() == elem2.GetRawText();
        }
    }
}

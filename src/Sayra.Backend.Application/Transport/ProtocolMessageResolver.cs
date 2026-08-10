using System;
using System.Text.Json;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Application.Transport
{
    public static class ProtocolMessageResolver
    {
        public static object ResolveAndDeserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ProtocolException(ProtocolException.InvalidJson, "Empty or whitespace JSON string.");
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new ProtocolException(ProtocolException.InvalidJson, $"Invalid JSON structure: {ex.Message}");
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new ProtocolException(ProtocolException.InvalidJson, "JSON root is not an object.");
                }

                // Check for "type" or "command" properties (case-insensitive)
                string? type = null;
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name.Equals("type", StringComparison.OrdinalIgnoreCase) ||
                        prop.Name.Equals("command", StringComparison.OrdinalIgnoreCase))
                    {
                        type = prop.Value.GetString();
                        break;
                    }
                }

                if (type != null)
                {
                    return ResolveByType(type, json);
                }

                // If no "type", check other distinguishing properties
                if (HasProperty(root, "payload") && HasProperty(root, "signature") && HasProperty(root, "timestamp"))
                {
                    return Deserialize<SecureMessageEnvelope>(json);
                }

                if (HasProperty(root, "eventType"))
                {
                    return Deserialize<EventMessage>(json);
                }

                if (HasProperty(root, "batchId") && HasProperty(root, "items"))
                {
                    return Deserialize<OfflineBatchRequest>(json);
                }

                if (HasProperty(root, "batchId") && HasProperty(root, "processedCount") && HasProperty(root, "success"))
                {
                    return Deserialize<OfflineBatchAcknowledgment>(json);
                }

                if (HasProperty(root, "commandId") && HasProperty(root, "status") && HasProperty(root, "message"))
                {
                    return Deserialize<ExecutionResultMessage>(json);
                }

                if (HasProperty(root, "cpu") && HasProperty(root, "ram") && HasProperty(root, "uptime"))
                {
                    return Deserialize<TelemetryModel>(json);
                }

                if (HasProperty(root, "version") && HasProperty(root, "issuedBy") && HasProperty(root, "signature"))
                {
                    return Deserialize<ConfigurationPackageContract>(json);
                }

                if (HasProperty(root, "version") && HasProperty(root, "packageUrl") && HasProperty(root, "checksum"))
                {
                    return Deserialize<UpdateManifest>(json);
                }

                if (HasProperty(root, "code") && HasProperty(root, "message") && HasProperty(root, "timestamp"))
                {
                    return Deserialize<ErrorResponseContract>(json);
                }

                throw new ProtocolException(ProtocolException.UnknownMessageType, "Could not resolve message type from JSON structure.");
            }
        }

        private static object ResolveByType(string type, string json)
        {
            switch (type.ToUpperInvariant())
            {
                case "PING":
                    return Deserialize<PingMessage>(json);
                case "DISCOVER_SAYRA_SERVER":
                    return Deserialize<DiscoveryRequest>(json);
                case "SAYRA_SERVER_RESPONSE":
                    return Deserialize<DiscoveryResponse>(json);
                case "AUTH_CHALLENGE":
                    return Deserialize<AuthChallengeMessage>(json);
                case "AUTH_RESPONSE":
                    return Deserialize<AuthResponseMessage>(json);
                case "AUTH_STATUS":
                    return Deserialize<AuthStatusMessage>(json);
                case "HEARTBEAT":
                    return Deserialize<HeartbeatMessage>(json);
                case "PONG":
                    return Deserialize<PongMessage>(json);
                case "START_SESSION":
                    return Deserialize<CommandMessage<StartSessionPayload>>(json);
                case "RUN_APP":
                    return Deserialize<CommandMessage<RunAppPayload>>(json);
                case "KILL_APP":
                    return Deserialize<CommandMessage<KillAppPayload>>(json);
                default:
                    throw new ProtocolException(ProtocolException.UnknownMessageType, $"Unknown message type: {type}");
            }
        }

        private static bool HasProperty(JsonElement element, string name)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static T Deserialize<T>(string json)
        {
            try
            {
                var val = ProtocolSerialization.Deserialize<T>(json);
                if (val == null)
                {
                    throw new ProtocolException(ProtocolException.InvalidMessage, $"Failed to deserialize to {typeof(T).Name}.");
                }
                return val;
            }
            catch (JsonException ex)
            {
                throw new ProtocolException(ProtocolException.InvalidJson, $"JSON structure failed validation for {typeof(T).Name}: {ex.Message}");
            }
        }
    }
}

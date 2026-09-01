using System;

namespace Sayra.Backend.Contracts
{
    public class ClientEventEnvelopeDto
    {
        public string EventId { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string WorkstationId { get; set; } = string.Empty;
        public string? SessionId { get; set; }
        public string CorrelationId { get; set; } = string.Empty;
        public DateTime OccurredAt { get; set; }
        public string Payload { get; set; } = "{}";
    }

    public static class ClientEventType
    {
        public const string ClientStarted = "CLIENT_STARTED";
        public const string ClientStopped = "CLIENT_STOPPED";
        public const string ApplicationStarted = "APPLICATION_STARTED";
        public const string ApplicationExited = "APPLICATION_EXITED";
        public const string ApplicationCrashed = "APPLICATION_CRASHED";
        public const string WorkstationStateChanged = "WORKSTATION_STATE_CHANGED";
        public const string ConfigurationChanged = "CONFIGURATION_CHANGED";
        public const string SecurityEvent = "SECURITY_EVENT";
        public const string NetworkChanged = "NETWORK_CHANGED";
        public const string DeviceChanged = "DEVICE_CHANGED";
        public const string DiagnosticEvent = "DIAGNOSTIC_EVENT";
        public const string SessionRuntimeEvent = "SESSION_RUNTIME_EVENT";
    }
}

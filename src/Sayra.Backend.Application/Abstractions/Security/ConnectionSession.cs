using System;
using Sayra.Backend.Application.Abstractions.Transport;

namespace Sayra.Backend.Application.Abstractions.Security
{
    public class ConnectionSession
    {
        public string ConnectionId { get; set; } = string.Empty;
        public string? PcId { get; set; }
        public byte[]? SessionKey { get; set; }
        public ConnectionLifecycleState HandshakeState { get; set; }
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;

        // Challenge and timestamp for verification
        public string PendingChallenge { get; set; } = string.Empty;
        public DateTime ChallengeCreatedAt { get; set; } = DateTime.UtcNow;
    }
}

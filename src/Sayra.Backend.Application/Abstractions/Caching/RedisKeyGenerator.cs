using System;

namespace Sayra.Backend.Application.Abstractions.Caching
{
    public static class RedisKeyGenerator
    {
        private const string VersionPrefix = "v1";

        public static string WorkstationStateKey(Guid workstationId) =>
            $"{VersionPrefix}:workstation:{workstationId:N}:state";

        public static string ConnectionStateKey(Guid connectionId) =>
            $"{VersionPrefix}:connection:{connectionId:N}:state";

        public static string HeartbeatStateKey(Guid workstationId) =>
            $"{VersionPrefix}:heartbeat:{workstationId:N}:state";

        public static string CommandStateKey(Guid commandId) =>
            $"{VersionPrefix}:command:{commandId:N}:state";

        public static string IdempotencyKey(string key) =>
            $"{VersionPrefix}:idempotency:{key}:state";
    }
}

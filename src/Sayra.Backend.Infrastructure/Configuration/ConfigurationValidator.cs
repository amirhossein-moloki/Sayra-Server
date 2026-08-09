using System;
using Sayra.Backend.Infrastructure.Configuration.Options;

namespace Sayra.Backend.Infrastructure.Configuration
{
    public static class ConfigurationValidator
    {
        public static void Validate(
            DatabaseOptions databaseOptions,
            RedisOptions redisOptions,
            ServerOptions serverOptions,
            DiscoveryOptions discoveryOptions)
        {
            if (databaseOptions == null)
                throw new InvalidOperationException("Critical configuration section 'Database' is missing.");

            if (string.IsNullOrWhiteSpace(databaseOptions.ConnectionString))
                throw new InvalidOperationException("Critical configuration setting 'Database:ConnectionString' is missing or empty.");

            if (redisOptions == null)
                throw new InvalidOperationException("Critical configuration section 'Redis' is missing.");

            if (string.IsNullOrWhiteSpace(redisOptions.ConnectionString))
                throw new InvalidOperationException("Critical configuration setting 'Redis:ConnectionString' is missing or empty.");

            if (serverOptions == null)
                throw new InvalidOperationException("Critical configuration section 'Server' is missing.");

            if (serverOptions.Port <= 0 || serverOptions.Port > 65535)
                throw new InvalidOperationException($"Invalid critical configuration 'Server:Port': {serverOptions.Port}. Must be between 1 and 65535.");

            if (discoveryOptions == null)
                throw new InvalidOperationException("Critical configuration section 'Discovery' is missing.");

            if (discoveryOptions.UdpPort <= 0 || discoveryOptions.UdpPort > 65535)
                throw new InvalidOperationException($"Invalid critical configuration 'Discovery:UdpPort': {discoveryOptions.UdpPort}. Must be between 1 and 65535.");
        }
    }
}

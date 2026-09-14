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
            DiscoveryOptions discoveryOptions,
            SecurityOptions? securityOptions = null)
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

            if (serverOptions.HandshakeTimeout < 1)
                throw new InvalidOperationException($"Invalid configuration 'Server:HandshakeTimeout': {serverOptions.HandshakeTimeout}. Must be at least 1 second.");

            if (serverOptions.ConnectionTimeout < 1)
                throw new InvalidOperationException($"Invalid configuration 'Server:ConnectionTimeout': {serverOptions.ConnectionTimeout}. Must be at least 1 second.");

            if (serverOptions.MaximumConnections < 1)
                throw new InvalidOperationException($"Invalid configuration 'Server:MaximumConnections': {serverOptions.MaximumConnections}. Must be at least 1.");

            if (serverOptions.ReceiveBufferSize < 1024)
                throw new InvalidOperationException($"Invalid configuration 'Server:ReceiveBufferSize': {serverOptions.ReceiveBufferSize}. Must be at least 1024 bytes.");

            if (serverOptions.SendBufferSize < 1024)
                throw new InvalidOperationException($"Invalid configuration 'Server:SendBufferSize': {serverOptions.SendBufferSize}. Must be at least 1024 bytes.");

            if (serverOptions.MaximumMessageSize < 1024)
                throw new InvalidOperationException($"Invalid configuration 'Server:MaximumMessageSize': {serverOptions.MaximumMessageSize}. Must be at least 1024 bytes.");

            if (serverOptions.HeartbeatInterval <= 0)
                throw new InvalidOperationException($"Invalid configuration 'Server:HeartbeatInterval': {serverOptions.HeartbeatInterval}. Must be greater than 0 seconds.");

            if (serverOptions.HeartbeatTimeout <= serverOptions.HeartbeatInterval)
                throw new InvalidOperationException($"Invalid configuration 'Server:HeartbeatTimeout': {serverOptions.HeartbeatTimeout}. Must be strictly greater than HeartbeatInterval ({serverOptions.HeartbeatInterval}s).");

            if (serverOptions.HeartbeatGracePeriod < 0)
                throw new InvalidOperationException($"Invalid configuration 'Server:HeartbeatGracePeriod': {serverOptions.HeartbeatGracePeriod}. Cannot be negative.");

            if (serverOptions.LivenessCheckInterval <= 0)
                throw new InvalidOperationException($"Invalid configuration 'Server:LivenessCheckInterval': {serverOptions.LivenessCheckInterval}. Must be greater than 0 seconds.");

            if (discoveryOptions == null)
                throw new InvalidOperationException("Critical configuration section 'Discovery' is missing.");

            if (discoveryOptions.UdpPort <= 0 || discoveryOptions.UdpPort > 65535)
                throw new InvalidOperationException($"Invalid critical configuration 'Discovery:UdpPort': {discoveryOptions.UdpPort}. Must be between 1 and 65535.");

            if (securityOptions != null)
            {
                ValidateSecurityOptions(securityOptions);
            }
        }

        public static void Validate(
            DatabaseOptions databaseOptions,
            RedisOptions redisOptions,
            ServerOptions serverOptions,
            DiscoveryOptions discoveryOptions,
            SecurityOptions securityOptions,
            ResilienceOptions resilienceOptions)
        {
            Validate(databaseOptions, redisOptions, serverOptions, discoveryOptions, securityOptions);
            if (resilienceOptions != null)
            {
                ValidateResilienceOptions(resilienceOptions);
            }
        }

        public static void ValidateResilienceOptions(ResilienceOptions resilienceOptions)
        {
            if (resilienceOptions == null)
                throw new InvalidOperationException("Configuration section 'Resilience' is missing.");

            if (resilienceOptions.MaxRetryAttempts < 0 || resilienceOptions.MaxRetryAttempts > 10)
                throw new InvalidOperationException($"Invalid configuration 'Resilience:MaxRetryAttempts': {resilienceOptions.MaxRetryAttempts}. Must be between 0 and 10.");

            if (resilienceOptions.InitialBackoffSeconds <= 0)
                throw new InvalidOperationException($"Invalid configuration 'Resilience:InitialBackoffSeconds': {resilienceOptions.InitialBackoffSeconds}. Must be greater than 0.");

            if (resilienceOptions.MaxBackoffSeconds < resilienceOptions.InitialBackoffSeconds)
                throw new InvalidOperationException($"Invalid configuration 'Resilience:MaxBackoffSeconds': {resilienceOptions.MaxBackoffSeconds}. Must be >= InitialBackoffSeconds.");

            if (resilienceOptions.JitterFactor < 0 || resilienceOptions.JitterFactor > 1.0)
                throw new InvalidOperationException($"Invalid configuration 'Resilience:JitterFactor': {resilienceOptions.JitterFactor}. Must be between 0 and 1.0.");

            if (resilienceOptions.OverallTimeoutSeconds <= 0)
                throw new InvalidOperationException($"Invalid configuration 'Resilience:OverallTimeoutSeconds': {resilienceOptions.OverallTimeoutSeconds}. Must be greater than 0.");

            if (resilienceOptions.AttemptTimeoutSeconds <= 0)
                throw new InvalidOperationException($"Invalid configuration 'Resilience:AttemptTimeoutSeconds': {resilienceOptions.AttemptTimeoutSeconds}. Must be greater than 0.");

            if (resilienceOptions.CircuitBreakerFailureThreshold < 1)
                throw new InvalidOperationException($"Invalid configuration 'Resilience:CircuitBreakerFailureThreshold': {resilienceOptions.CircuitBreakerFailureThreshold}. Must be at least 1.");

            if (resilienceOptions.CircuitBreakerBreakDurationSeconds <= 0)
                throw new InvalidOperationException($"Invalid configuration 'Resilience:CircuitBreakerBreakDurationSeconds': {resilienceOptions.CircuitBreakerBreakDurationSeconds}. Must be greater than 0.");
        }

        public static void ValidateSecurityOptions(SecurityOptions securityOptions)
        {
            if (securityOptions == null)
                throw new InvalidOperationException("Critical configuration section 'Security' is missing.");

            if (string.IsNullOrWhiteSpace(securityOptions.PasswordHashAlgorithm))
                throw new InvalidOperationException("Security:PasswordHashAlgorithm cannot be empty.");

            if (securityOptions.ArgonDegreeOfParallelism < 1)
                throw new InvalidOperationException($"Invalid Security:ArgonDegreeOfParallelism: {securityOptions.ArgonDegreeOfParallelism}. Must be at least 1.");

            if (securityOptions.ArgonMemorySizeKb < 8192)
                throw new InvalidOperationException($"Invalid Security:ArgonMemorySizeKb: {securityOptions.ArgonMemorySizeKb}. Must be at least 8192 KB.");

            if (securityOptions.ArgonIterations < 1)
                throw new InvalidOperationException($"Invalid Security:ArgonIterations: {securityOptions.ArgonIterations}. Must be at least 1.");

            if (securityOptions.SaltSize < 16)
                throw new InvalidOperationException($"Invalid Security:SaltSize: {securityOptions.SaltSize}. Must be at least 16 bytes.");

            if (securityOptions.KeySize < 16)
                throw new InvalidOperationException($"Invalid Security:KeySize: {securityOptions.KeySize}. Must be at least 16 bytes.");

            if (securityOptions.MaxPasswordLength < 8 || securityOptions.MaxPasswordLength > 4096)
                throw new InvalidOperationException($"Invalid Security:MaxPasswordLength: {securityOptions.MaxPasswordLength}. Must be between 8 and 4096 characters.");
        }
    }
}

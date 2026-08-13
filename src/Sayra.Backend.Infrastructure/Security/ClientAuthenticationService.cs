using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Workstations;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Infrastructure.Security
{
    public class ClientAuthenticationService : IClientAuthenticationService
    {
        private readonly ICryptographicService _cryptographicService;
        private readonly IConfiguration _configuration;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<ClientAuthenticationService> _logger;
        private readonly string _masterKey;

        // Tracks current active connection sessions during the handshake phase
        private readonly ConcurrentDictionary<string, ConnectionSession> _sessions = new();

        // Tracks failed login attempts per connection ID
        private readonly ConcurrentDictionary<string, int> _failedAttempts = new();

        private const int MaxFailedAttempts = 3;
        private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromSeconds(30);

        public ClientAuthenticationService(
            ICryptographicService cryptographicService,
            IConfiguration configuration,
            IServiceScopeFactory serviceScopeFactory,
            ILogger<ClientAuthenticationService> logger)
        {
            _cryptographicService = cryptographicService ?? throw new ArgumentNullException(nameof(cryptographicService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _masterKey = _configuration["SAYRA_MASTER_KEY"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(_masterKey))
            {
                throw new InvalidOperationException("Critical configuration 'SAYRA_MASTER_KEY' is missing or empty.");
            }
        }

        public Task<string> GenerateChallengeAsync(ITcpConnection connection, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Generating cryptographically secure challenge for connection {ConnectionId}.", connection.ConnectionId);

            // 1. Max Failed Attempts Check
            if (_failedAttempts.TryGetValue(connection.ConnectionId, out int attempts) && attempts >= MaxFailedAttempts)
            {
                _logger.LogWarning("Connection {ConnectionId} blocked due to too many failed authentication attempts.", connection.ConnectionId);
                throw new AuthenticationException("AUTH_FAILED", "Too many failed authentication attempts.");
            }

            // 2. Generate random 32-byte challenge
            byte[] challengeBytes = RandomNumberGenerator.GetBytes(32);
            string challengeBase64 = Convert.ToBase64String(challengeBytes);

            // 3. Store pending challenge inside connection session
            var session = new ConnectionSession
            {
                ConnectionId = connection.ConnectionId,
                HandshakeState = ConnectionLifecycleState.Authenticating,
                LastActivity = DateTime.UtcNow,
                PendingChallenge = challengeBase64,
                ChallengeCreatedAt = DateTime.UtcNow
            };

            _sessions[connection.ConnectionId] = session;

            // Update connection state
            connection.UpdateState(ConnectionLifecycleState.Authenticating);

            return Task.FromResult(challengeBase64);
        }

        public async Task<AuthenticationResult> ValidateResponseAsync(ITcpConnection connection, AuthResponseDto response, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Evaluating AUTH_RESPONSE for connection {ConnectionId}...", connection.ConnectionId);

            // 1. Retrieve connection session
            if (!_sessions.TryGetValue(connection.ConnectionId, out var session))
            {
                _logger.LogWarning("No active authentication session found for connection {ConnectionId}.", connection.ConnectionId);
                return Fail(connection, "AUTH_FAILED", "Authentication session not found.");
            }

            // 2. Check challenge lifetime
            if (DateTime.UtcNow - session.ChallengeCreatedAt > ChallengeLifetime)
            {
                _logger.LogWarning("Authentication challenge expired for connection {ConnectionId}.", connection.ConnectionId);
                return Fail(connection, "AUTH_FAILED", "Authentication challenge expired.");
            }

            // 3. Validate HMAC-SHA256
            byte[] masterKeyBytes = Encoding.UTF8.GetBytes(_masterKey);
            byte[] challengeStringToSign = Encoding.UTF8.GetBytes(session.PendingChallenge);
            byte[] expectedHmac = _cryptographicService.ComputeHmacSha256(challengeStringToSign, masterKeyBytes);

            byte[] clientHmacBytes;
            try
            {
                clientHmacBytes = Convert.FromBase64String(response.Hmac);
            }
            catch (FormatException)
            {
                _logger.LogWarning("Connection {ConnectionId} sent invalid Base64 for HMAC.", connection.ConnectionId);
                return Fail(connection, "AUTH_FAILED", "Invalid HMAC Base64.");
            }

            // Constant-time HMAC comparison
            if (!CryptographicOperations.FixedTimeEquals(expectedHmac, clientHmacBytes))
            {
                _logger.LogWarning("HMAC verification failed for connection {ConnectionId}.", connection.ConnectionId);
                return Fail(connection, "AUTH_FAILED", "HMAC challenge response verification failed.");
            }

            // 4. Decrypt SessionKey
            byte[] aesKey = masterKeyBytes.Length == 32 ? masterKeyBytes : SHA256.HashData(masterKeyBytes);
            byte[] ivBytes;
            byte[] encryptedSessionKeyBytes;

            try
            {
                byte[] rawPayloadBytes = Convert.FromBase64String(response.EncryptedSessionKey);

                // Supporting both separate IV vs. prepended IV (first 16 bytes of EncryptedSessionKey)
                if (string.IsNullOrEmpty(response.Iv))
                {
                    if (rawPayloadBytes.Length < 16)
                    {
                        _logger.LogWarning("Connection {ConnectionId} sent EncryptedSessionKey too short to extract IV.", connection.ConnectionId);
                        return Fail(connection, "AUTH_FAILED", "Encrypted session key payload is too short.");
                    }
                    ivBytes = new byte[16];
                    encryptedSessionKeyBytes = new byte[rawPayloadBytes.Length - 16];
                    Buffer.BlockCopy(rawPayloadBytes, 0, ivBytes, 0, 16);
                    Buffer.BlockCopy(rawPayloadBytes, 16, encryptedSessionKeyBytes, 0, encryptedSessionKeyBytes.Length);
                }
                else
                {
                    ivBytes = Convert.FromBase64String(response.Iv);
                    encryptedSessionKeyBytes = rawPayloadBytes;
                }
            }
            catch (FormatException ex)
            {
                _logger.LogWarning(ex, "Connection {ConnectionId} sent invalid Base64 for cryptographic payload fields.", connection.ConnectionId);
                return Fail(connection, "AUTH_FAILED", "Invalid cryptographic Base64 fields.");
            }

            if (ivBytes.Length != 16)
            {
                _logger.LogWarning("Connection {ConnectionId} sent IV of invalid length: {Length} bytes.", connection.ConnectionId, ivBytes.Length);
                return Fail(connection, "AUTH_FAILED", "Invalid IV length.");
            }

            byte[] decryptedSessionKey;
            try
            {
                decryptedSessionKey = _cryptographicService.DecryptAes256Cbc(encryptedSessionKeyBytes, aesKey, ivBytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SessionKey decryption failed for connection {ConnectionId}.", connection.ConnectionId);
                return Fail(connection, "AUTH_FAILED", "SessionKey decryption failed.");
            }

            if (decryptedSessionKey.Length != 32)
            {
                _logger.LogWarning("Decrypted SessionKey for connection {ConnectionId} has invalid length: {Length} bytes.", connection.ConnectionId, decryptedSessionKey.Length);
                return Fail(connection, "AUTH_FAILED", "Invalid SessionKey length.");
            }

            // 5. Device Identity Verification & Authorization Check
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var authorizeHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<AuthorizeWorkstationCommand, Workstation>>();
                var bindHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<BindWorkstationConnectionCommand, Workstation>>();

                // Verify if device exists and is authorized (not disabled)
                var authResult = await authorizeHandler.HandleAsync(new AuthorizeWorkstationCommand { PcId = response.PcId }, cancellationToken);
                if (!authResult.IsSuccess)
                {
                    var errorCode = authResult.ErrorCode == "DEVICE_NOT_REGISTERED" ? "DEVICE_NOT_REGISTERED" : "AUTH_FAILED";
                    _logger.LogWarning("Device authorization failed for PC-ID {PcId}: {ErrorCode} - {Message}", response.PcId, errorCode, authResult.ErrorMessage);
                    return Fail(connection, errorCode, authResult.ErrorMessage ?? "Device authorization failed.");
                }

                // Perform connection binding
                var bindResult = await bindHandler.HandleAsync(new BindWorkstationConnectionCommand
                {
                    PcId = response.PcId,
                    ConnectionId = connection.ConnectionId,
                    SiteId = response.SiteId ?? string.Empty,
                    Hostname = response.Hostname ?? string.Empty,
                    ClientVersion = response.ClientVersion ?? string.Empty,
                    IpAddress = connection.RemoteIpAddress ?? "127.0.0.1"
                }, cancellationToken);

                if (!bindResult.IsSuccess)
                {
                    _logger.LogWarning("Device binding failed for PC-ID {PcId}: {Message}", response.PcId, bindResult.ErrorMessage);
                    return Fail(connection, "AUTH_FAILED", bindResult.ErrorMessage ?? "Device binding failed.");
                }
            }

            // 6. Transition State of Session
            session.PcId = response.PcId;
            session.SessionKey = decryptedSessionKey;
            session.HandshakeState = ConnectionLifecycleState.Authenticated;
            session.LastActivity = DateTime.UtcNow;

            connection.SessionKey = decryptedSessionKey;
            connection.PcId = response.PcId;
            connection.Hostname = response.Hostname;
            connection.SiteId = response.SiteId;
            connection.ClientVersion = response.ClientVersion;

            connection.UpdateState(ConnectionLifecycleState.Authenticated);

            // Clean up tracking on successful login to prevent memory leak
            CleanupSession(connection.ConnectionId);

            _logger.LogInformation("Handshake completed successfully. Transitioned connection {ConnectionId} to Authenticated.", connection.ConnectionId);

            return new AuthenticationResult
            {
                IsSuccess = true,
                SessionKey = decryptedSessionKey,
                NewState = ConnectionLifecycleState.Authenticated
            };
        }

        public void CleanupSession(string connectionId)
        {
            _sessions.TryRemove(connectionId, out _);
            _failedAttempts.TryRemove(connectionId, out _);
        }

        private AuthenticationResult Fail(ITcpConnection connection, string errorCode, string message)
        {
            _failedAttempts.AddOrUpdate(connection.ConnectionId, 1, (_, count) => count + 1);

            _logger.LogWarning("Security Event: Authentication failed for connection {ConnectionId}. Reason: {Reason}", connection.ConnectionId, message);

            // Clean up transient session details on validation failure to prevent memory leak
            _sessions.TryRemove(connection.ConnectionId, out _);

            return new AuthenticationResult
            {
                IsSuccess = false,
                ErrorCode = errorCode,
                ErrorMessage = message,
                NewState = ConnectionLifecycleState.Disconnected
            };
        }
    }
}

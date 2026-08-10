using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Workstations;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Infrastructure.Security
{
    public class TcpAuthenticationService : ITcpAuthenticationService
    {
        private readonly ICryptographicService _cryptographicService;
        private readonly IRedisService _redisService;
        private readonly IConfiguration _configuration;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<TcpAuthenticationService> _logger;
        private readonly string _masterKey;

        public TcpAuthenticationService(
            ICryptographicService cryptographicService,
            IRedisService redisService,
            IConfiguration configuration,
            IServiceScopeFactory serviceScopeFactory,
            ILogger<TcpAuthenticationService> logger)
        {
            _cryptographicService = cryptographicService ?? throw new ArgumentNullException(nameof(cryptographicService));
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _masterKey = _configuration["SAYRA_MASTER_KEY"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(_masterKey))
            {
                throw new InvalidOperationException("Critical configuration 'SAYRA_MASTER_KEY' is missing or empty.");
            }
        }

        public async Task<bool> AuthenticateAsync(ITcpConnection connection, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting authentication handshake for connection {ConnectionId}...", connection.ConnectionId);
            connection.UpdateState(ConnectionLifecycleState.Authenticating);

            Stream stream;
            try
            {
                stream = connection.GetStream();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get network stream for connection {ConnectionId}.", connection.ConnectionId);
                return false;
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5)); // Handshake timeout

                // 1. Generate random 32-byte challenge
                byte[] challengeBytes = RandomNumberGenerator.GetBytes(32);
                string challengeBase64 = Convert.ToBase64String(challengeBytes);

                // 2. Send AUTH_CHALLENGE
                var challengeDto = new AuthChallengeDto
                {
                    Type = "AUTH_CHALLENGE",
                    Challenge = challengeBase64
                };
                string challengeJson = JsonSerializer.Serialize(challengeDto) + "\n";
                byte[] challengeSendBytes = Encoding.UTF8.GetBytes(challengeJson);
                await stream.WriteAsync(challengeSendBytes, 0, challengeSendBytes.Length, cts.Token);
                await stream.FlushAsync(cts.Token);
                _logger.LogDebug("Sent AUTH_CHALLENGE to connection {ConnectionId}.", connection.ConnectionId);

                // 3. Read AUTH_RESPONSE
                string? responseLine = await ReadLineWithLimitAsync(stream, 8192, cts.Token); // 8KB limit
                if (string.IsNullOrEmpty(responseLine))
                {
                    _logger.LogWarning("Connection {ConnectionId} closed or timed out before sending AUTH_RESPONSE.", connection.ConnectionId);
                    await SendAuthStatusAndCloseAsync(connection, stream, "AUTH_FAILED", "Timeout or empty response", cts.Token);
                    return false;
                }

                AuthResponseDto? responseDto;
                try
                {
                    responseDto = JsonSerializer.Deserialize<AuthResponseDto>(responseLine, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Connection {ConnectionId} sent malformed JSON in AUTH_RESPONSE.", connection.ConnectionId);
                    await SendAuthStatusAndCloseAsync(connection, stream, "AUTH_FAILED", "Malformed JSON payload", cts.Token);
                    return false;
                }

                if (responseDto == null)
                {
                    _logger.LogWarning("Connection {ConnectionId} sent empty AUTH_RESPONSE.", connection.ConnectionId);
                    await SendAuthStatusAndCloseAsync(connection, stream, "AUTH_FAILED", "Empty payload", cts.Token);
                    return false;
                }

                // 4. Validate HMAC-SHA256
                byte[] masterKeyBytes = Encoding.UTF8.GetBytes(_masterKey);
                byte[] challengeStringToSign = Encoding.UTF8.GetBytes(challengeBase64);
                byte[] expectedHmac = _cryptographicService.ComputeHmacSha256(challengeStringToSign, masterKeyBytes);

                byte[] clientHmacBytes;
                try
                {
                    clientHmacBytes = Convert.FromBase64String(responseDto.Hmac);
                }
                catch (FormatException)
                {
                    _logger.LogWarning("Connection {ConnectionId} sent invalid Base64 for HMAC.", connection.ConnectionId);
                    await SendAuthStatusAndCloseAsync(connection, stream, "AUTH_FAILED", "Invalid HMAC Base64", cts.Token);
                    return false;
                }

                // Constant-time HMAC comparison
                if (!CryptographicOperations.FixedTimeEquals(expectedHmac, clientHmacBytes))
                {
                    _logger.LogWarning("HMAC verification failed for connection {ConnectionId}. Expected signature does not match.", connection.ConnectionId);
                    await SendAuthStatusAndCloseAsync(connection, stream, "AUTH_FAILED", "HMAC challenge response verification failed", cts.Token);
                    return false;
                }

                // 5. Decrypt SessionKey
                byte[] aesKey = masterKeyBytes.Length == 32 ? masterKeyBytes : SHA256.HashData(masterKeyBytes);
                byte[] ivBytes;
                byte[] encryptedSessionKeyBytes;

                try
                {
                    ivBytes = Convert.FromBase64String(responseDto.Iv);
                    encryptedSessionKeyBytes = Convert.FromBase64String(responseDto.EncryptedSessionKey);
                }
                catch (FormatException)
                {
                    _logger.LogWarning("Connection {ConnectionId} sent invalid Base64 for IV or EncryptedSessionKey.", connection.ConnectionId);
                    await SendAuthStatusAndCloseAsync(connection, stream, "AUTH_FAILED", "Invalid cryptographic Base64 fields", cts.Token);
                    return false;
                }

                if (ivBytes.Length != 16)
                {
                    _logger.LogWarning("Connection {ConnectionId} sent IV of invalid length: {Length} bytes.", connection.ConnectionId, ivBytes.Length);
                    await SendAuthStatusAndCloseAsync(connection, stream, "AUTH_FAILED", "Invalid IV length", cts.Token);
                    return false;
                }

                byte[] decryptedSessionKey;
                try
                {
                    decryptedSessionKey = _cryptographicService.DecryptAes256Cbc(encryptedSessionKeyBytes, aesKey, ivBytes);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SessionKey decryption failed for connection {ConnectionId}.", connection.ConnectionId);
                    await SendAuthStatusAndCloseAsync(connection, stream, "AUTH_FAILED", "SessionKey decryption failed", cts.Token);
                    return false;
                }

                if (decryptedSessionKey.Length != 32)
                {
                    _logger.LogWarning("Decrypted SessionKey for connection {ConnectionId} has invalid length: {Length} bytes.", connection.ConnectionId, decryptedSessionKey.Length);
                    await SendAuthStatusAndCloseAsync(connection, stream, "AUTH_FAILED", "Invalid SessionKey length", cts.Token);
                    return false;
                }

                // 6. Device Identity Verification & Authorization Check
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var authorizeHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<AuthorizeWorkstationCommand, Workstation>>();
                    var bindHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<BindWorkstationConnectionCommand, Workstation>>();

                    // Verify if device exists and is authorized (not disabled)
                    var authResult = await authorizeHandler.HandleAsync(new AuthorizeWorkstationCommand { PcId = responseDto.PcId }, cts.Token);
                    if (!authResult.IsSuccess)
                    {
                        var errorCode = authResult.ErrorCode == "DEVICE_NOT_REGISTERED" ? "DEVICE_NOT_REGISTERED" : "AUTH_FAILED";
                        _logger.LogWarning("Device authorization failed for PC-ID {PcId}: {ErrorCode} - {Message}", responseDto.PcId, errorCode, authResult.ErrorMessage);
                        await SendAuthStatusAndCloseAsync(connection, stream, errorCode, authResult.ErrorMessage ?? "Device authorization failed", cts.Token);
                        return false;
                    }

                    // Perform connection binding, replace any concurrent connection, update state to Online
                    var bindResult = await bindHandler.HandleAsync(new BindWorkstationConnectionCommand
                    {
                        PcId = responseDto.PcId,
                        ConnectionId = connection.ConnectionId,
                        SiteId = responseDto.SiteId ?? string.Empty,
                        Hostname = responseDto.Hostname ?? string.Empty,
                        ClientVersion = responseDto.ClientVersion ?? string.Empty
                    }, cts.Token);

                    if (!bindResult.IsSuccess)
                    {
                        _logger.LogWarning("Device binding failed for PC-ID {PcId}: {Message}", responseDto.PcId, bindResult.ErrorMessage);
                        await SendAuthStatusAndCloseAsync(connection, stream, "AUTH_FAILED", bindResult.ErrorMessage ?? "Device binding failed", cts.Token);
                        return false;
                    }
                }

                // 7. Bind Identity & Store SessionKey in persistent Connection context
                connection.SessionKey = decryptedSessionKey;
                connection.PcId = responseDto.PcId;
                connection.Hostname = responseDto.Hostname;
                connection.SiteId = responseDto.SiteId;
                connection.ClientVersion = responseDto.ClientVersion;

                connection.UpdateState(ConnectionLifecycleState.Authenticated);
                _logger.LogInformation("Connection {ConnectionId} successfully authenticated. Device PC-ID: {PcId}, Host: {Hostname}.", connection.ConnectionId, responseDto.PcId, responseDto.Hostname);

                // Send success AUTH_STATUS
                var successStatus = new AuthStatusDto
                {
                    Type = "AUTH_STATUS",
                    Status = "SUCCESS",
                    ErrorCode = null,
                    Message = "Authentication successful"
                };
                string successJson = JsonSerializer.Serialize(successStatus) + "\n";
                byte[] successBytes = Encoding.UTF8.GetBytes(successJson);
                await stream.WriteAsync(successBytes, 0, successBytes.Length, cts.Token);
                await stream.FlushAsync(cts.Token);

                // Transition to Active State
                connection.UpdateState(ConnectionLifecycleState.Active);

                // Cache connection state metadata in Redis securely
                try
                {
                    if (Guid.TryParse(connection.ConnectionId, out var connectionGuid))
                    {
                        var redisKey = RedisKeyGenerator.ConnectionStateKey(connectionGuid);
                        var metadata = new ConnectionStateMetadata
                        {
                            ConnectionId = connection.ConnectionId,
                            State = "Active",
                            PcId = connection.PcId,
                            Hostname = connection.Hostname,
                            SiteId = connection.SiteId,
                            ClientVersion = connection.ClientVersion,
                            AuthenticatedAt = DateTime.UtcNow
                        };
                        await _redisService.SetAsync(redisKey, metadata, TimeSpan.FromHours(24));
                    }
                }
                catch (Exception redisEx)
                {
                    _logger.LogWarning(redisEx, "Failed to cache connection state in Redis for {ConnectionId}.", connection.ConnectionId);
                }

                return true;
            }
            catch (DeviceNotRegisteredException ex)
            {
                _logger.LogWarning("Device not registered during handshake for connection {ConnectionId}. Error: {Message}", connection.ConnectionId, ex.Message);
                await SendAuthStatusAndCloseAsync(connection, stream, "DEVICE_NOT_REGISTERED", ex.Message, CancellationToken.None);
                return false;
            }
            catch (AuthFailedException ex)
            {
                _logger.LogWarning("Device authorization failed during handshake for connection {ConnectionId}. Error: {Message}", connection.ConnectionId, ex.Message);
                await SendAuthStatusAndCloseAsync(connection, stream, "AUTH_FAILED", ex.Message, CancellationToken.None);
                return false;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Authentication handshake timed out or was canceled for connection {ConnectionId}.", connection.ConnectionId);
                connection.UpdateState(ConnectionLifecycleState.Disconnected);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during authentication handshake for connection {ConnectionId}.", connection.ConnectionId);
                connection.UpdateState(ConnectionLifecycleState.Disconnected);
                return false;
            }
        }

        private async Task SendAuthStatusAndCloseAsync(
            ITcpConnection connection, Stream stream, string errorCode, string message, CancellationToken cancellationToken)
        {
            try
            {
                var failureStatus = new AuthStatusDto
                {
                    Type = "AUTH_STATUS",
                    Status = "FAILED",
                    ErrorCode = errorCode,
                    Message = message
                };
                string failureJson = JsonSerializer.Serialize(failureStatus) + "\n";
                byte[] failureBytes = Encoding.UTF8.GetBytes(failureJson);
                await stream.WriteAsync(failureBytes, 0, failureBytes.Length, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            catch
            {
                // Ignore failures trying to write to failing socket
            }

            connection.UpdateState(ConnectionLifecycleState.Disconnected);
        }

        private static async Task<string?> ReadLineWithLimitAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
        {
            var ms = new MemoryStream();
            int totalBytes = 0;
            byte[] singleBuffer = new byte[1];

            while (totalBytes < maxBytes)
            {
                int read = await stream.ReadAsync(singleBuffer, 0, 1, cancellationToken);
                if (read == 0)
                {
                    break; // EOF
                }

                byte b = singleBuffer[0];
                if (b == (byte)'\n')
                {
                    break; // Newline found
                }

                if (b != (byte)'\r')
                {
                    ms.WriteByte(b);
                    totalBytes++;
                }
            }

            if (totalBytes >= maxBytes)
            {
                throw new InvalidOperationException("Payload limit exceeded during read operation.");
            }

            if (totalBytes == 0 && ms.Length == 0)
            {
                return null;
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    public class ConnectionStateMetadata
    {
        public string ConnectionId { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string? PcId { get; set; }
        public string? Hostname { get; set; }
        public string? SiteId { get; set; }
        public string? ClientVersion { get; set; }
        public DateTime AuthenticatedAt { get; set; }
    }
}

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
        private readonly IClientAuthenticationService _clientAuthenticationService;
        private readonly IRedisService _redisService;
        private readonly ILogger<TcpAuthenticationService> _logger;

        public TcpAuthenticationService(
            IClientAuthenticationService clientAuthenticationService,
            IRedisService redisService,
            ILogger<TcpAuthenticationService> logger)
        {
            _clientAuthenticationService = clientAuthenticationService ?? throw new ArgumentNullException(nameof(clientAuthenticationService));
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> AuthenticateAsync(ITcpConnection connection, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting secure TCP authentication handshake delegation for connection {ConnectionId}...", connection.ConnectionId);

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

            bool isSuccess = false;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5)); // Handshake timeout

                // 1. Generate challenge using delegated authentication service
                string challengeBase64 = await _clientAuthenticationService.GenerateChallengeAsync(connection, cts.Token);

                // 2. Send AUTH_CHALLENGE to the stream
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

                // 3. Read AUTH_RESPONSE from stream
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

                // 4. Validate the response using delegated authentication service
                var authResult = await _clientAuthenticationService.ValidateResponseAsync(connection, responseDto, cts.Token);
                if (!authResult.IsSuccess)
                {
                    _logger.LogWarning("Authentication failed for connection {ConnectionId}: {ErrorCode} - {Message}", connection.ConnectionId, authResult.ErrorCode, authResult.ErrorMessage);
                    await SendAuthStatusAndCloseAsync(connection, stream, authResult.ErrorCode ?? "AUTH_FAILED", authResult.ErrorMessage ?? "Authentication failed", cts.Token);
                    return false;
                }

                _logger.LogInformation("Connection {ConnectionId} successfully authenticated. Device PC-ID: {PcId}.", connection.ConnectionId, connection.PcId);

                // 5. Send success AUTH_STATUS to client
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

                // 6. Transition Connection Lifecycle State to Active
                connection.UpdateState(ConnectionLifecycleState.Active);

                // 7. Cache connection state metadata in Redis securely
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
                            AuthenticatedAt = DateTime.UtcNow,
                            ConnectedAt = DateTime.UtcNow,
                            LastActivity = DateTime.UtcNow
                        };
                        await _redisService.SetAsync(redisKey, metadata, TimeSpan.FromHours(24));
                    }
                }
                catch (Exception redisEx)
                {
                    _logger.LogWarning(redisEx, "Failed to cache connection state in Redis for {ConnectionId}.", connection.ConnectionId);
                }

                isSuccess = true;
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
            catch (AuthenticationException ex)
            {
                _logger.LogWarning("AuthenticationException during handshake for connection {ConnectionId}. Error: {Message}", connection.ConnectionId, ex.Message);
                await SendAuthStatusAndCloseAsync(connection, stream, ex.ErrorCode, ex.Message, CancellationToken.None);
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
            finally
            {
                if (!isSuccess)
                {
                    _clientAuthenticationService.CleanupSession(connection.ConnectionId);
                }
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
        public DateTime ConnectedAt { get; set; }
        public DateTime LastActivity { get; set; }
    }
}

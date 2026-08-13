using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Sayra.Backend.Api;
using Sayra.Backend.Domain;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Security;
using Sayra.Backend.Infrastructure.Transport;

namespace Sayra.Backend.IntegrationTests
{
    public class HandshakeAndSecurityTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly ITcpAuthenticationService _authService;
        private readonly ICryptographicService _cryptoService;
        private readonly IRedisService _redisService;
        private readonly ITcpConnectionRegistry _connectionRegistry;
        private readonly ISecureMessageService _secureMessageService;
        private readonly string _masterKey;

        public HandshakeAndSecurityTests(WebApplicationFactory<Program> factory)
        {
            EnvLoader.Load();
            _factory = factory;

            var config = _factory.Services.GetRequiredService<IConfiguration>();
            _masterKey = config["SAYRA_MASTER_KEY"] ?? "SuperSecretMasterKey32BytesLong123!";

            _authService = _factory.Services.GetRequiredService<ITcpAuthenticationService>();
            _cryptoService = _factory.Services.GetRequiredService<ICryptographicService>();
            _redisService = _factory.Services.GetRequiredService<IRedisService>();
            _connectionRegistry = _factory.Services.GetRequiredService<ITcpConnectionRegistry>();
            _secureMessageService = _factory.Services.GetRequiredService<ISecureMessageService>();

            // Seed test workstations to database so they are authorized during handshake tests
            using (var scope = _factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.ApplicationDbContext>();
                var pcIds = new[] { "PC-TEST-01", "PC-SECURE-MSG" };
                foreach (var pcId in pcIds)
                {
                    var existing = dbContext.Workstations.FirstOrDefault(w => w.PcId == pcId);
                    if (existing == null)
                    {
                        dbContext.Workstations.Add(new Workstation
                        {
                            PcId = pcId,
                            Name = pcId,
                            SiteId = "SITE-ALPHA",
                            Hostname = "DESKTOP-TEST",
                            MacAddress = $"00:1A:2B:3C:4D:{Random.Shared.Next(10, 99)}",
                            IpAddress = "127.0.0.1",
                            Status = "Offline",
                            IsDisabled = false
                        });
                    }
                    else
                    {
                        existing.IsDisabled = false;
                        existing.Status = "Offline";
                    }
                }
                dbContext.SaveChanges();
            }
        }

        #region Helper Methods

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private async Task<(TcpServer Server, int Port)> StartTestServerAsync()
        {
            int port = GetFreePort();
            var serverOpts = Options.Create(new ServerOptions { Port = port, Host = "127.0.0.1" });
            var tlsOpts = Options.Create(new TlsOptions());
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var serverLogger = loggerFactory.CreateLogger<TcpServer>();

            var server = new TcpServer(
                _connectionRegistry,
                _authService,
                _cryptoService,
                _redisService,
                _secureMessageService,
                serverOpts,
                tlsOpts,
                serverLogger);

            await server.StartAsync(CancellationToken.None);
            return (server, port);
        }

        private static async Task<string> ReadLineWithTimeoutAsync(NetworkStream stream, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            var ms = new MemoryStream();
            byte[] singleBuffer = new byte[1];

            while (true)
            {
                int read = await stream.ReadAsync(singleBuffer, 0, 1, cts.Token);
                if (read == 0) break;

                byte b = singleBuffer[0];
                if (b == (byte)'\n') break;
                if (b != (byte)'\r')
                {
                    ms.WriteByte(b);
                }
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        #endregion

        #region Handshake & Authentication Tests

        [Fact]
        public async Task Valid_Handshake_Should_Succeed_And_Transition_State_And_Cache_In_Redis()
        {
            // Arrange
            var (server, port) = await StartTestServerAsync();
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            var stream = client.GetStream();

            try
            {
                // 1. Read AUTH_CHALLENGE from server
                string challengeLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var challengeDto = JsonSerializer.Deserialize<AuthChallengeDto>(challengeLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Assert.NotNull(challengeDto);
                Assert.Equal("AUTH_CHALLENGE", challengeDto.Type);
                Assert.False(string.IsNullOrEmpty(challengeDto.Challenge));

                // 2. Build AUTH_RESPONSE
                byte[] masterKeyBytes = Encoding.UTF8.GetBytes(_masterKey);
                byte[] challengeStringToSign = Encoding.UTF8.GetBytes(challengeDto.Challenge);
                byte[] expectedHmacBytes = _cryptoService.ComputeHmacSha256(challengeStringToSign, masterKeyBytes);
                string responseHmacBase64 = Convert.ToBase64String(expectedHmacBytes);

                // Generate random SessionKey
                byte[] sessionKey = RandomNumberGenerator.GetBytes(32);
                byte[] iv = RandomNumberGenerator.GetBytes(16);
                byte[] aesKey = masterKeyBytes.Length == 32 ? masterKeyBytes : SHA256.HashData(masterKeyBytes);
                byte[] encryptedSessionKey = _cryptoService.EncryptAes256Cbc(sessionKey, aesKey, iv);

                var responseDto = new AuthResponseDto
                {
                    Hmac = responseHmacBase64,
                    EncryptedSessionKey = Convert.ToBase64String(encryptedSessionKey),
                    Iv = Convert.ToBase64String(iv),
                    PcId = "PC-TEST-01",
                    Hostname = "DESKTOP-TEST",
                    SiteId = "SITE-ALPHA",
                    ClientVersion = "1.0.0"
                };

                string responseJson = JsonSerializer.Serialize(responseDto) + "\n";
                byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson);
                await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                await stream.FlushAsync();

                // 3. Read AUTH_STATUS from server
                string statusLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var statusDto = JsonSerializer.Deserialize<AuthStatusDto>(statusLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Assert.NotNull(statusDto);
                Assert.Equal("AUTH_STATUS", statusDto.Type);
                Assert.Equal("SUCCESS", statusDto.Status);
                Assert.Null(statusDto.ErrorCode);

                // 4. Verify connection registry and lifecycle state
                await Task.Delay(100);
                Assert.Equal(1, _connectionRegistry.Count);
                ITcpConnection? conn = null;
                foreach (var c in _connectionRegistry.GetAll())
                {
                    conn = c;
                    break;
                }
                Assert.NotNull(conn);
                Assert.Equal(ConnectionLifecycleState.Active, conn.State);
                Assert.Equal("PC-TEST-01", conn.PcId);
                Assert.Equal("DESKTOP-TEST", conn.Hostname);
                Assert.Equal("SITE-ALPHA", conn.SiteId);
                Assert.Equal("1.0.0", conn.ClientVersion);
                Assert.True(CryptographicOperations.FixedTimeEquals(sessionKey, conn.SessionKey!));

                // 5. Verify Redis connection state caching
                var redisKey = RedisKeyGenerator.ConnectionStateKey(Guid.Parse(conn.ConnectionId));
                var cachedState = await _redisService.GetAsync<ConnectionStateMetadata>(redisKey);
                Assert.NotNull(cachedState);
                Assert.Equal("Active", cachedState.State);
                Assert.Equal("PC-TEST-01", cachedState.PcId);
                Assert.Equal("DESKTOP-TEST", cachedState.Hostname);
            }
            finally
            {
                client.Close();
                await Task.Delay(100);
                await server.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task Invalid_HMAC_Should_Fail_Handshake_And_Close_Connection()
        {
            var (server, port) = await StartTestServerAsync();
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            var stream = client.GetStream();

            try
            {
                string challengeLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));

                // Tampered HMAC (random 32 bytes)
                byte[] fakeHmac = RandomNumberGenerator.GetBytes(32);
                byte[] sessionKey = RandomNumberGenerator.GetBytes(32);
                byte[] iv = RandomNumberGenerator.GetBytes(16);
                byte[] aesKey = Encoding.UTF8.GetBytes(_masterKey).Length == 32 ? Encoding.UTF8.GetBytes(_masterKey) : SHA256.HashData(Encoding.UTF8.GetBytes(_masterKey));
                byte[] encryptedSessionKey = _cryptoService.EncryptAes256Cbc(sessionKey, aesKey, iv);

                var responseDto = new AuthResponseDto
                {
                    Hmac = Convert.ToBase64String(fakeHmac),
                    EncryptedSessionKey = Convert.ToBase64String(encryptedSessionKey),
                    Iv = Convert.ToBase64String(iv),
                    PcId = "PC-TEST-01",
                    Hostname = "DESKTOP-TEST"
                };

                string responseJson = JsonSerializer.Serialize(responseDto) + "\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(responseJson));
                await stream.FlushAsync();

                // Read AUTH_STATUS (Should be FAILED)
                string statusLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var statusDto = JsonSerializer.Deserialize<AuthStatusDto>(statusLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Assert.NotNull(statusDto);
                Assert.Equal("FAILED", statusDto.Status);
                Assert.Equal("AUTH_FAILED", statusDto.ErrorCode);

                // Verify connection closes
                int read = await stream.ReadAsync(new byte[1], 0, 1);
                Assert.Equal(0, read); // EOF
            }
            finally
            {
                client.Close();
                await Task.Delay(100);
                await server.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task Invalid_EncryptedSessionKey_Should_Fail_Handshake_And_Close_Connection()
        {
            var (server, port) = await StartTestServerAsync();
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            var stream = client.GetStream();

            try
            {
                string challengeLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var challengeDto = JsonSerializer.Deserialize<AuthChallengeDto>(challengeLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                byte[] challengeStringToSign = Encoding.UTF8.GetBytes(challengeDto!.Challenge);
                byte[] expectedHmacBytes = _cryptoService.ComputeHmacSha256(challengeStringToSign, Encoding.UTF8.GetBytes(_masterKey));

                // Send fake SessionKey ciphertext that cannot be decrypted or is too short
                byte[] fakeEncryptedSessionKey = new byte[8]; // Invalid size
                byte[] iv = RandomNumberGenerator.GetBytes(16);

                var responseDto = new AuthResponseDto
                {
                    Hmac = Convert.ToBase64String(expectedHmacBytes),
                    EncryptedSessionKey = Convert.ToBase64String(fakeEncryptedSessionKey),
                    Iv = Convert.ToBase64String(iv),
                    PcId = "PC-TEST-01",
                    Hostname = "DESKTOP-TEST"
                };

                string responseJson = JsonSerializer.Serialize(responseDto) + "\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(responseJson));
                await stream.FlushAsync();

                string statusLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var statusDto = JsonSerializer.Deserialize<AuthStatusDto>(statusLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Assert.NotNull(statusDto);
                Assert.Equal("FAILED", statusDto.Status);
                Assert.Equal("AUTH_FAILED", statusDto.ErrorCode);

                int read = await stream.ReadAsync(new byte[1], 0, 1);
                Assert.Equal(0, read);
            }
            finally
            {
                client.Close();
                await Task.Delay(100);
                await server.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task Invalid_SessionKey_Length_Should_Fail_Handshake()
        {
            var (server, port) = await StartTestServerAsync();
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            var stream = client.GetStream();

            try
            {
                string challengeLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var challengeDto = JsonSerializer.Deserialize<AuthChallengeDto>(challengeLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                byte[] challengeStringToSign = Encoding.UTF8.GetBytes(challengeDto!.Challenge);
                byte[] expectedHmacBytes = _cryptoService.ComputeHmacSha256(challengeStringToSign, Encoding.UTF8.GetBytes(_masterKey));

                // SessionKey of invalid length (e.g. 16 bytes instead of 32)
                byte[] shortSessionKey = RandomNumberGenerator.GetBytes(16);
                byte[] iv = RandomNumberGenerator.GetBytes(16);
                byte[] aesKey = Encoding.UTF8.GetBytes(_masterKey).Length == 32 ? Encoding.UTF8.GetBytes(_masterKey) : SHA256.HashData(Encoding.UTF8.GetBytes(_masterKey));
                byte[] encryptedSessionKey = _cryptoService.EncryptAes256Cbc(shortSessionKey, aesKey, iv);

                var responseDto = new AuthResponseDto
                {
                    Hmac = Convert.ToBase64String(expectedHmacBytes),
                    EncryptedSessionKey = Convert.ToBase64String(encryptedSessionKey),
                    Iv = Convert.ToBase64String(iv),
                    PcId = "PC-TEST-01",
                    Hostname = "DESKTOP-TEST"
                };

                string responseJson = JsonSerializer.Serialize(responseDto) + "\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(responseJson));
                await stream.FlushAsync();

                string statusLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var statusDto = JsonSerializer.Deserialize<AuthStatusDto>(statusLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Assert.NotNull(statusDto);
                Assert.Equal("FAILED", statusDto.Status);
                Assert.Equal("AUTH_FAILED", statusDto.ErrorCode);
            }
            finally
            {
                client.Close();
                await Task.Delay(100);
                await server.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task Handshake_OversizedPayload_Should_Be_Rejected()
        {
            var (server, port) = await StartTestServerAsync();
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            var stream = client.GetStream();

            try
            {
                // Read challenge first
                await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));

                // Send massive junk string (> 8KB)
                byte[] oversizedJunk = new byte[10000];
                Array.Fill(oversizedJunk, (byte)'A');
                oversizedJunk[9999] = (byte)'\n';

                await stream.WriteAsync(oversizedJunk, 0, oversizedJunk.Length);
                await stream.FlushAsync();

                // Server should terminate connection immediately due to read limit
                int read = await stream.ReadAsync(new byte[1], 0, 1);
                Assert.Equal(0, read);
            }
            finally
            {
                client.Close();
                await Task.Delay(100);
                await server.StopAsync(CancellationToken.None);
            }
        }

        #endregion

        #region Post-Authentication Cryptography Tests

        [Fact]
        public async Task Valid_SecureMessageEnvelope_Should_Be_Decrypted_And_Processed()
        {
            var (server, port) = await StartTestServerAsync();
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            var stream = client.GetStream();

            try
            {
                // 1. Handshake Phase
                string challengeLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var challengeDto = JsonSerializer.Deserialize<AuthChallengeDto>(challengeLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                byte[] masterKeyBytes = Encoding.UTF8.GetBytes(_masterKey);
                byte[] challengeStringToSign = Encoding.UTF8.GetBytes(challengeDto!.Challenge);
                byte[] expectedHmacBytes = _cryptoService.ComputeHmacSha256(challengeStringToSign, masterKeyBytes);

                byte[] sessionKey = RandomNumberGenerator.GetBytes(32);
                byte[] iv = RandomNumberGenerator.GetBytes(16);
                byte[] aesKey = masterKeyBytes.Length == 32 ? masterKeyBytes : SHA256.HashData(masterKeyBytes);
                byte[] encryptedSessionKey = _cryptoService.EncryptAes256Cbc(sessionKey, aesKey, iv);

                var responseDto = new AuthResponseDto
                {
                    Hmac = Convert.ToBase64String(expectedHmacBytes),
                    EncryptedSessionKey = Convert.ToBase64String(encryptedSessionKey),
                    Iv = Convert.ToBase64String(iv),
                    PcId = "PC-SECURE-MSG",
                    Hostname = "DESKTOP-SECURE"
                };

                await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responseDto) + "\n"));
                await stream.FlushAsync();

                // Read AUTH_STATUS success
                await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));

                // 2. Post-Authentication Phase: Send SecureMessageEnvelope
                string plaintextMsg = "{\"command\":\"PING\"}";
                byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintextMsg);

                // Encrypt payload using session key
                byte[] msgIv = RandomNumberGenerator.GetBytes(16);
                byte[] encryptedCiphertext = _cryptoService.EncryptAes256Cbc(plaintextBytes, sessionKey, msgIv);

                // Prepend IV to ciphertext before Base64
                byte[] prependedPayloadBytes = new byte[16 + encryptedCiphertext.Length];
                Buffer.BlockCopy(msgIv, 0, prependedPayloadBytes, 0, 16);
                Buffer.BlockCopy(encryptedCiphertext, 0, prependedPayloadBytes, 16, encryptedCiphertext.Length);
                string payloadBase64 = Convert.ToBase64String(prependedPayloadBytes);

                string timestampIso = DateTime.UtcNow.ToString("o");

                // Signature = HMAC-SHA256(Payload + "|" + Timestamp) using sessionKey
                string signatureInput = payloadBase64 + "|" + timestampIso;
                byte[] computedSignature = _cryptoService.ComputeHmacSha256(Encoding.UTF8.GetBytes(signatureInput), sessionKey);
                string signatureBase64 = Convert.ToBase64String(computedSignature);

                var envelope = new Sayra.Backend.Application.Abstractions.Security.SecureMessageEnvelope
                {
                    Payload = payloadBase64,
                    Signature = signatureBase64,
                    Timestamp = timestampIso
                };

                string envelopeJson = JsonSerializer.Serialize(envelope) + "\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(envelopeJson));
                await stream.FlushAsync();

                // Give server time to read and verify (should not close connection)
                await Task.Delay(200);
                Assert.Equal(1, _connectionRegistry.Count);
            }
            finally
            {
                client.Close();
                await Task.Delay(100);
                await server.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task Tampered_Payload_Should_Be_Rejected_And_Terminate_Connection()
        {
            var (server, port) = await StartTestServerAsync();
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            var stream = client.GetStream();

            try
            {
                // Handshake
                string challengeLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var challengeDto = JsonSerializer.Deserialize<AuthChallengeDto>(challengeLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                byte[] masterKeyBytes = Encoding.UTF8.GetBytes(_masterKey);
                byte[] challengeStringToSign = Encoding.UTF8.GetBytes(challengeDto!.Challenge);
                byte[] expectedHmacBytes = _cryptoService.ComputeHmacSha256(challengeStringToSign, masterKeyBytes);
                byte[] sessionKey = RandomNumberGenerator.GetBytes(32);
                byte[] iv = RandomNumberGenerator.GetBytes(16);
                byte[] aesKey = masterKeyBytes.Length == 32 ? masterKeyBytes : SHA256.HashData(masterKeyBytes);
                byte[] encryptedSessionKey = _cryptoService.EncryptAes256Cbc(sessionKey, aesKey, iv);
                var responseDto = new AuthResponseDto
                {
                    Hmac = Convert.ToBase64String(expectedHmacBytes),
                    EncryptedSessionKey = Convert.ToBase64String(encryptedSessionKey),
                    Iv = Convert.ToBase64String(iv),
                    PcId = "PC-SECURE-MSG"
                };
                await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responseDto) + "\n"));
                await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));

                // Post-Auth with Tampered Payload
                string plaintextMsg = "{\"command\":\"PING\"}";
                byte[] msgIv = RandomNumberGenerator.GetBytes(16);
                byte[] encryptedCiphertext = _cryptoService.EncryptAes256Cbc(Encoding.UTF8.GetBytes(plaintextMsg), sessionKey, msgIv);
                byte[] prependedPayloadBytes = new byte[16 + encryptedCiphertext.Length];
                Buffer.BlockCopy(msgIv, 0, prependedPayloadBytes, 0, 16);
                Buffer.BlockCopy(encryptedCiphertext, 0, prependedPayloadBytes, 16, encryptedCiphertext.Length);
                string payloadBase64 = Convert.ToBase64String(prependedPayloadBytes);
                string timestampIso = DateTime.UtcNow.ToString("o");

                string signatureInput = payloadBase64 + "|" + timestampIso;
                byte[] computedSignature = _cryptoService.ComputeHmacSha256(Encoding.UTF8.GetBytes(signatureInput), sessionKey);

                // Tamper with payload after signature generation
                string tamperedPayloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("TAMPERED_JUNK_PAYLOAD_HERE_123456789"));

                var envelope = new Sayra.Backend.Application.Abstractions.Security.SecureMessageEnvelope
                {
                    Payload = tamperedPayloadBase64,
                    Signature = Convert.ToBase64String(computedSignature),
                    Timestamp = timestampIso
                };

                await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope) + "\n"));
                await stream.FlushAsync();

                // Server must reject and close connection immediately
                int read = await stream.ReadAsync(new byte[1], 0, 1);
                Assert.Equal(0, read); // EOF
            }
            finally
            {
                client.Close();
                await Task.Delay(100);
                await server.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task Stale_Timestamp_Should_Be_Rejected()
        {
            var (server, port) = await StartTestServerAsync();
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            var stream = client.GetStream();

            try
            {
                // Handshake
                string challengeLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var challengeDto = JsonSerializer.Deserialize<AuthChallengeDto>(challengeLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                byte[] masterKeyBytes = Encoding.UTF8.GetBytes(_masterKey);
                byte[] challengeStringToSign = Encoding.UTF8.GetBytes(challengeDto!.Challenge);
                byte[] expectedHmacBytes = _cryptoService.ComputeHmacSha256(challengeStringToSign, masterKeyBytes);
                byte[] sessionKey = RandomNumberGenerator.GetBytes(32);
                byte[] iv = RandomNumberGenerator.GetBytes(16);
                byte[] aesKey = masterKeyBytes.Length == 32 ? masterKeyBytes : SHA256.HashData(masterKeyBytes);
                byte[] encryptedSessionKey = _cryptoService.EncryptAes256Cbc(sessionKey, aesKey, iv);
                var responseDto = new AuthResponseDto
                {
                    Hmac = Convert.ToBase64String(expectedHmacBytes),
                    EncryptedSessionKey = Convert.ToBase64String(encryptedSessionKey),
                    Iv = Convert.ToBase64String(iv),
                    PcId = "PC-SECURE-MSG"
                };
                await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responseDto) + "\n"));
                await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));

                // Send stale timestamp (10 minutes ago, drift = 600 seconds)
                string plaintextMsg = "{\"command\":\"PING\"}";
                byte[] msgIv = RandomNumberGenerator.GetBytes(16);
                byte[] encryptedCiphertext = _cryptoService.EncryptAes256Cbc(Encoding.UTF8.GetBytes(plaintextMsg), sessionKey, msgIv);
                byte[] prependedPayloadBytes = new byte[16 + encryptedCiphertext.Length];
                Buffer.BlockCopy(msgIv, 0, prependedPayloadBytes, 0, 16);
                Buffer.BlockCopy(encryptedCiphertext, 0, prependedPayloadBytes, 16, encryptedCiphertext.Length);
                string payloadBase64 = Convert.ToBase64String(prependedPayloadBytes);

                string staleTimestampIso = DateTime.UtcNow.AddMinutes(-10).ToString("o");

                string signatureInput = payloadBase64 + "|" + staleTimestampIso;
                byte[] computedSignature = _cryptoService.ComputeHmacSha256(Encoding.UTF8.GetBytes(signatureInput), sessionKey);

                var envelope = new Sayra.Backend.Application.Abstractions.Security.SecureMessageEnvelope
                {
                    Payload = payloadBase64,
                    Signature = Convert.ToBase64String(computedSignature),
                    Timestamp = staleTimestampIso
                };

                await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope) + "\n"));
                await stream.FlushAsync();

                // Server must reject and close connection immediately
                int read = await stream.ReadAsync(new byte[1], 0, 1);
                Assert.Equal(0, read); // EOF
            }
            finally
            {
                client.Close();
                await Task.Delay(100);
                await server.StopAsync(CancellationToken.None);
            }
        }

        #endregion

        #region Transport Edge Cases

        [Fact]
        public async Task Fragmented_TCP_Frame_Should_Be_Accumulated_And_Processed()
        {
            var (server, port) = await StartTestServerAsync();
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            var stream = client.GetStream();

            try
            {
                // Handshake
                string challengeLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var challengeDto = JsonSerializer.Deserialize<AuthChallengeDto>(challengeLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                byte[] masterKeyBytes = Encoding.UTF8.GetBytes(_masterKey);
                byte[] challengeStringToSign = Encoding.UTF8.GetBytes(challengeDto!.Challenge);
                byte[] expectedHmacBytes = _cryptoService.ComputeHmacSha256(challengeStringToSign, masterKeyBytes);
                byte[] sessionKey = RandomNumberGenerator.GetBytes(32);
                byte[] iv = RandomNumberGenerator.GetBytes(16);
                byte[] aesKey = masterKeyBytes.Length == 32 ? masterKeyBytes : SHA256.HashData(masterKeyBytes);
                byte[] encryptedSessionKey = _cryptoService.EncryptAes256Cbc(sessionKey, aesKey, iv);
                var responseDto = new AuthResponseDto
                {
                    Hmac = Convert.ToBase64String(expectedHmacBytes),
                    EncryptedSessionKey = Convert.ToBase64String(encryptedSessionKey),
                    Iv = Convert.ToBase64String(iv),
                    PcId = "PC-SECURE-MSG"
                };

                // Write handshake in two fragments to verify robustness during handshake phase
                string responseJson = JsonSerializer.Serialize(responseDto) + "\n";
                byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson);

                int split = responseBytes.Length / 2;
                await stream.WriteAsync(responseBytes, 0, split);
                await stream.FlushAsync();
                await Task.Delay(50); // slight fragmentation delay

                await stream.WriteAsync(responseBytes, split, responseBytes.Length - split);
                await stream.FlushAsync();

                // Read AUTH_STATUS success
                string statusLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var statusDto = JsonSerializer.Deserialize<AuthStatusDto>(statusLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Assert.NotNull(statusDto);
                Assert.Equal("SUCCESS", statusDto.Status);
            }
            finally
            {
                client.Close();
                await Task.Delay(100);
                await server.StopAsync(CancellationToken.None);
            }
        }

        #endregion

        #region UDP Discovery & Heartbeat Integration Tests

        [Fact]
        public async Task UdpDiscovery_ValidRequest_Should_Return_ValidSignatureResponse()
        {
            // Arrange
            int udpPort = GetFreePort();
            var discoveryOpts = Options.Create(new DiscoveryOptions { Enabled = true, UdpPort = udpPort });
            var serverOpts = Options.Create(new ServerOptions { Port = 5000 });
            var config = _factory.Services.GetRequiredService<IConfiguration>();
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var udpLogger = loggerFactory.CreateLogger<UdpDiscoveryServer>();

            using var udpServer = new UdpDiscoveryServer(discoveryOpts, serverOpts, config, udpLogger);
            await udpServer.StartAsync(CancellationToken.None);

            using var client = new UdpClient();
            client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

            var request = new
            {
                type = "DISCOVER_SAYRA_SERVER",
                clientId = "WORKSTATION-01",
                timestamp = DateTime.UtcNow.ToString("o"),
                nonce = "UUID-NONCE-123456"
            };

            string requestJson = JsonSerializer.Serialize(request);
            byte[] requestBytes = Encoding.UTF8.GetBytes(requestJson);

            // Act
            await client.SendAsync(requestBytes, requestBytes.Length, "127.0.0.1", udpPort);

            var receiveResult = await client.ReceiveAsync(new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token);

            // Assert
            string responseJson = Encoding.UTF8.GetString(receiveResult.Buffer);
            var response = JsonSerializer.Deserialize<Sayra.Backend.Contracts.DiscoveryResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(response);
            Assert.Equal("SAYRA_SERVER_RESPONSE", response.Type);
            Assert.Equal("UUID-NONCE-123456", response.Nonce);
            Assert.False(string.IsNullOrEmpty(response.Signature));

            // Verify Signature calculation matches
            byte[] masterKeyBytes = Encoding.UTF8.GetBytes(_masterKey);
            string timestampStr = response.Timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ");
            string expectedInput = $"{response.ServerId}|{response.Ip}|{response.TcpPort}|{timestampStr}";
            byte[] expectedInputBytes = Encoding.UTF8.GetBytes(expectedInput);

            byte[] expectedHmac;
            using (var hmac = new HMACSHA256(masterKeyBytes))
            {
                expectedHmac = hmac.ComputeHash(expectedInputBytes);
            }
            string expectedSignatureBase64 = Convert.ToBase64String(expectedHmac);

            Assert.Equal(expectedSignatureBase64, response.Signature);

            await udpServer.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task UdpDiscovery_MalformedRequest_Should_Be_Safely_Ignored()
        {
            // Arrange
            int udpPort = GetFreePort();
            var discoveryOpts = Options.Create(new DiscoveryOptions { Enabled = true, UdpPort = udpPort });
            var serverOpts = Options.Create(new ServerOptions { Port = 5000 });
            var config = _factory.Services.GetRequiredService<IConfiguration>();
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var udpLogger = loggerFactory.CreateLogger<UdpDiscoveryServer>();

            using var udpServer = new UdpDiscoveryServer(discoveryOpts, serverOpts, config, udpLogger);
            await udpServer.StartAsync(CancellationToken.None);

            using var client = new UdpClient();
            client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

            // Malformed and non-matching requests
            byte[][] payloads = new byte[][]
            {
                Encoding.UTF8.GetBytes("INVALID_PAYLOAD_NOT_JSON"),
                Encoding.UTF8.GetBytes("{\"type\":\"WRONG_TYPE\"}"),
                new byte[] { 0x01, 0x02, 0x03, 0xFF }
            };

            foreach (var payload in payloads)
            {
                await client.SendAsync(payload, payload.Length, "127.0.0.1", udpPort);
            }

            // Give a little time for processing to happen (no exceptions should crash the server)
            await Task.Delay(100);

            // Verify server is still running fine by sending a valid request now
            var validRequest = new
            {
                type = "DISCOVER_SAYRA_SERVER",
                clientId = "WORKSTATION-01",
                timestamp = DateTime.UtcNow.ToString("o"),
                nonce = "UUID-NONCE-OK"
            };
            byte[] validBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(validRequest));
            await client.SendAsync(validBytes, validBytes.Length, "127.0.0.1", udpPort);

            var receiveResult = await client.ReceiveAsync(new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token);
            string responseJson = Encoding.UTF8.GetString(receiveResult.Buffer);
            Assert.Contains("SAYRA_SERVER_RESPONSE", responseJson);

            await udpServer.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task PostAuth_Heartbeat_Should_Receive_Pong_And_Update_LastActivity_And_Redis()
        {
            var (server, port) = await StartTestServerAsync();
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            var stream = client.GetStream();

            try
            {
                // 1. Handshake Phase
                string challengeLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var challengeDto = JsonSerializer.Deserialize<AuthChallengeDto>(challengeLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                byte[] masterKeyBytes = Encoding.UTF8.GetBytes(_masterKey);
                byte[] challengeStringToSign = Encoding.UTF8.GetBytes(challengeDto!.Challenge);
                byte[] expectedHmacBytes = _cryptoService.ComputeHmacSha256(challengeStringToSign, masterKeyBytes);

                byte[] sessionKey = RandomNumberGenerator.GetBytes(32);
                byte[] iv = RandomNumberGenerator.GetBytes(16);
                byte[] aesKey = masterKeyBytes.Length == 32 ? masterKeyBytes : SHA256.HashData(masterKeyBytes);
                byte[] encryptedSessionKey = _cryptoService.EncryptAes256Cbc(sessionKey, aesKey, iv);

                var responseDto = new AuthResponseDto
                {
                    Hmac = Convert.ToBase64String(expectedHmacBytes),
                    EncryptedSessionKey = Convert.ToBase64String(encryptedSessionKey),
                    Iv = Convert.ToBase64String(iv),
                    PcId = "PC-SECURE-MSG",
                    Hostname = "DESKTOP-SECURE"
                };

                await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responseDto) + "\n"));
                await stream.FlushAsync();

                // Read AUTH_STATUS success
                await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));

                // Grab active connection for assertion later
                ITcpConnection? connection = null;
                foreach (var conn in _connectionRegistry.GetAll())
                {
                    if (conn.PcId == "PC-SECURE-MSG")
                    {
                        connection = conn;
                        break;
                    }
                }
                Assert.NotNull(connection);
                var initialLastActivity = connection.LastActivity;

                // 2. Heartbeat Request
                var heartbeatPayload = new
                {
                    type = "HEARTBEAT",
                    timestamp = DateTime.UtcNow.ToString("o")
                };
                string heartbeatJson = JsonSerializer.Serialize(heartbeatPayload);
                byte[] plaintextBytes = Encoding.UTF8.GetBytes(heartbeatJson);

                // Encrypt payload
                byte[] msgIv = RandomNumberGenerator.GetBytes(16);
                byte[] encryptedCiphertext = _cryptoService.EncryptAes256Cbc(plaintextBytes, sessionKey, msgIv);
                byte[] prependedPayloadBytes = new byte[16 + encryptedCiphertext.Length];
                Buffer.BlockCopy(msgIv, 0, prependedPayloadBytes, 0, 16);
                Buffer.BlockCopy(encryptedCiphertext, 0, prependedPayloadBytes, 16, encryptedCiphertext.Length);
                string payloadBase64 = Convert.ToBase64String(prependedPayloadBytes);

                string timestampIso = DateTime.UtcNow.ToString("o");
                string signatureInput = payloadBase64 + "|" + timestampIso;
                byte[] computedSignature = _cryptoService.ComputeHmacSha256(Encoding.UTF8.GetBytes(signatureInput), sessionKey);

                var envelope = new Sayra.Backend.Application.Abstractions.Security.SecureMessageEnvelope
                {
                    Payload = payloadBase64,
                    Signature = Convert.ToBase64String(computedSignature),
                    Timestamp = timestampIso
                };

                string envelopeJson = JsonSerializer.Serialize(envelope) + "\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(envelopeJson));
                await stream.FlushAsync();

                // 3. Read SecureMessageEnvelope response (PONG)
                string pongLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var pongEnvelope = JsonSerializer.Deserialize<Sayra.Backend.Application.Abstractions.Security.SecureMessageEnvelope>(pongLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Assert.NotNull(pongEnvelope);

                // Decrypt PONG envelope
                byte[] rawPongBytes = Convert.FromBase64String(pongEnvelope.Payload);
                byte[] pongIv = new byte[16];
                byte[] pongCiphertext = new byte[rawPongBytes.Length - 16];
                Buffer.BlockCopy(rawPongBytes, 0, pongIv, 0, 16);
                Buffer.BlockCopy(rawPongBytes, 16, pongCiphertext, 0, pongCiphertext.Length);

                byte[] decryptedPongBytes = _cryptoService.DecryptAes256Cbc(pongCiphertext, sessionKey, pongIv);
                string decryptedPongJson = Encoding.UTF8.GetString(decryptedPongBytes);

                var pongMessage = JsonSerializer.Deserialize<Sayra.Backend.Contracts.PongMessage>(decryptedPongJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Assert.NotNull(pongMessage);
                Assert.Equal("PONG", pongMessage.Type);

                // 4. Verify Connection updates
                Assert.True(connection.LastActivity >= initialLastActivity);

                // 5. Verify Redis connection state refreshed
                var redisKey = RedisKeyGenerator.ConnectionStateKey(Guid.Parse(connection.ConnectionId));
                var cachedState = await _redisService.GetAsync<Sayra.Backend.Infrastructure.Security.ConnectionStateMetadata>(redisKey);
                Assert.NotNull(cachedState);
                Assert.True(cachedState.LastActivity >= initialLastActivity);
            }
            finally
            {
                client.Close();
                await Task.Delay(100);
                await server.StopAsync(CancellationToken.None);
            }
        }

        #endregion
    }
}

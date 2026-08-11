using System;
using System.IO;
using System.Linq;
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
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Security;
using Sayra.Backend.Infrastructure.Transport;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.IntegrationTests
{
    public class TcpFramingIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly ITcpAuthenticationService _authService;
        private readonly ICryptographicService _cryptoService;
        private readonly IRedisService _redisService;
        private readonly ITcpConnectionRegistry _connectionRegistry;
        private readonly string _masterKey;

        public TcpFramingIntegrationTests(WebApplicationFactory<Program> factory)
        {
            EnvLoader.Load();
            _factory = factory;

            var config = _factory.Services.GetRequiredService<IConfiguration>();
            _masterKey = config["SAYRA_MASTER_KEY"] ?? "SuperSecretMasterKey32BytesLong123!";

            _authService = _factory.Services.GetRequiredService<ITcpAuthenticationService>();
            _cryptoService = _factory.Services.GetRequiredService<ICryptographicService>();
            _redisService = _factory.Services.GetRequiredService<IRedisService>();
            _connectionRegistry = _factory.Services.GetRequiredService<ITcpConnectionRegistry>();

            // Seed workstation for testing
            using (var scope = _factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.ApplicationDbContext>();
                var pcId = "PC-FRAMING-TEST";
                var existing = dbContext.Workstations.FirstOrDefault(w => w.PcId == pcId);
                if (existing == null)
                {
                    dbContext.Workstations.Add(new Workstation
                    {
                        PcId = pcId,
                        Name = pcId,
                        SiteId = "SITE-ALPHA",
                        Hostname = "DESKTOP-FRAMING",
                        MacAddress = "00:1A:2B:3C:4D:EF",
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
                dbContext.SaveChanges();
            }
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private async Task<(TcpServer Server, int Port)> StartTestServerAsync(int maxMessageSize = 65536)
        {
            int port = GetFreePort();
            var serverOpts = Options.Create(new ServerOptions { Port = port, Host = "127.0.0.1", MaxMessageSizeBytes = maxMessageSize });
            var tlsOpts = Options.Create(new TlsOptions());
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var serverLogger = loggerFactory.CreateLogger<TcpServer>();

            var server = new TcpServer(
                _connectionRegistry,
                _authService,
                _cryptoService,
                _redisService,
                serverOpts,
                tlsOpts,
                serverLogger,
                _factory.Services.GetRequiredService<IServiceScopeFactory>());

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

        private string CreateEncryptedEnvelopeJson(string plaintextMsg, byte[] sessionKey)
        {
            byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintextMsg);
            byte[] msgIv = RandomNumberGenerator.GetBytes(16);
            byte[] encryptedCiphertext = _cryptoService.EncryptAes256Cbc(plaintextBytes, sessionKey, msgIv);

            byte[] prependedPayloadBytes = new byte[16 + encryptedCiphertext.Length];
            Buffer.BlockCopy(msgIv, 0, prependedPayloadBytes, 0, 16);
            Buffer.BlockCopy(encryptedCiphertext, 0, prependedPayloadBytes, 16, encryptedCiphertext.Length);
            string payloadBase64 = Convert.ToBase64String(prependedPayloadBytes);

            string timestampIso = DateTime.UtcNow.ToString("o");

            string signatureInput = payloadBase64 + "|" + timestampIso;
            byte[] computedSignature = _cryptoService.ComputeHmacSha256(Encoding.UTF8.GetBytes(signatureInput), sessionKey);
            string signatureBase64 = Convert.ToBase64String(computedSignature);

            var envelope = new Sayra.Backend.Contracts.SecureMessageEnvelope
            {
                Payload = payloadBase64,
                Signature = signatureBase64,
                Timestamp = timestampIso
            };

            return JsonSerializer.Serialize(envelope);
        }

        [Fact]
        public async Task Server_Should_Correctly_Process_Coalesced_And_Fragmented_Frames()
        {
            // 1. Start Server
            var (server, port) = await StartTestServerAsync();
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            var stream = client.GetStream();

            try
            {
                // 2. Perform Handshake (Handshake uses plain \n framing)
                string challengeLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var challengeDto = JsonSerializer.Deserialize<AuthChallengeMessage>(challengeLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Assert.NotNull(challengeDto);

                byte[] masterKeyBytes = Encoding.UTF8.GetBytes(_masterKey);
                byte[] challengeStringToSign = Encoding.UTF8.GetBytes(challengeDto.Challenge);
                byte[] expectedHmacBytes = _cryptoService.ComputeHmacSha256(challengeStringToSign, masterKeyBytes);

                byte[] sessionKey = RandomNumberGenerator.GetBytes(32);
                byte[] iv = RandomNumberGenerator.GetBytes(16);
                byte[] aesKey = masterKeyBytes.Length == 32 ? masterKeyBytes : SHA256.HashData(masterKeyBytes);
                byte[] encryptedSessionKey = _cryptoService.EncryptAes256Cbc(sessionKey, aesKey, iv);

                var responseDto = new AuthResponseMessage
                {
                    Response = Convert.ToBase64String(expectedHmacBytes),
                    EncryptedSessionKey = Convert.ToBase64String(encryptedSessionKey),
                    Iv = Convert.ToBase64String(iv),
                    PcId = "PC-FRAMING-TEST",
                    Hostname = "DESKTOP-FRAMING"
                };

                string responseJson = JsonSerializer.Serialize(responseDto) + "\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(responseJson));
                await stream.FlushAsync();

                // Read AUTH_STATUS (SUCCESS)
                string statusLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var statusDto = JsonSerializer.Deserialize<AuthStatusMessage>(statusLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Assert.NotNull(statusDto);
                Assert.Equal("SUCCESS", statusDto.Status);

                // Now client is Authenticated. Any further frames must be SecureMessageEnvelopes.
                // 3. Test Coalesced secure messages (2 frames in 1 write)
                string msg1Plain = "{\"type\":\"HEARTBEAT\",\"pcId\":\"PC-FRAMING-TEST\",\"timestamp\":\"2026-10-18T12:00:00Z\"}";
                string msg2Plain = "{\"type\":\"PONG\",\"timestamp\":\"2026-10-18T12:00:05Z\"}";

                string envelope1Json = CreateEncryptedEnvelopeJson(msg1Plain, sessionKey);
                string envelope2Json = CreateEncryptedEnvelopeJson(msg2Plain, sessionKey);

                string coalescedWrite = envelope1Json + "\n" + envelope2Json + "\n";
                byte[] coalescedBytes = Encoding.UTF8.GetBytes(coalescedWrite);

                await stream.WriteAsync(coalescedBytes, 0, coalescedBytes.Length);
                await stream.FlushAsync();

                // Give server time to parse and resolve both
                await Task.Delay(200);

                // 4. Test Fragmented secure message (1 frame in multiple writes)
                string msg3Plain = "{\"type\":\"HEARTBEAT\",\"pcId\":\"PC-FRAMING-TEST\",\"timestamp\":\"2026-10-18T12:10:00Z\"}";
                string envelope3Json = CreateEncryptedEnvelopeJson(msg3Plain, sessionKey) + "\n";
                byte[] envelope3Bytes = Encoding.UTF8.GetBytes(envelope3Json);

                int split = envelope3Bytes.Length / 2;
                await stream.WriteAsync(envelope3Bytes, 0, split);
                await stream.FlushAsync();

                await Task.Delay(50); // fragmentation delay

                await stream.WriteAsync(envelope3Bytes, split, envelope3Bytes.Length - split);
                await stream.FlushAsync();

                // Give server time to parse and resolve
                await Task.Delay(200);

                // Connection should still be active and stable
                Assert.True(client.Connected);
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
        public async Task Server_Should_Reject_Oversized_Frame_Gracefully_And_Close_Connection()
        {
            // Start server with custom small message limit of 200 bytes
            var (server, port) = await StartTestServerAsync(maxMessageSize: 200);
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            var stream = client.GetStream();

            try
            {
                // 1. Perform Handshake
                string challengeLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var challengeDto = JsonSerializer.Deserialize<AuthChallengeMessage>(challengeLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Assert.NotNull(challengeDto);

                byte[] masterKeyBytes = Encoding.UTF8.GetBytes(_masterKey);
                byte[] challengeStringToSign = Encoding.UTF8.GetBytes(challengeDto.Challenge);
                byte[] expectedHmacBytes = _cryptoService.ComputeHmacSha256(challengeStringToSign, masterKeyBytes);

                byte[] sessionKey = RandomNumberGenerator.GetBytes(32);
                byte[] iv = RandomNumberGenerator.GetBytes(16);
                byte[] aesKey = masterKeyBytes.Length == 32 ? masterKeyBytes : SHA256.HashData(masterKeyBytes);
                byte[] encryptedSessionKey = _cryptoService.EncryptAes256Cbc(sessionKey, aesKey, iv);

                var responseDto = new AuthResponseMessage
                {
                    Response = Convert.ToBase64String(expectedHmacBytes),
                    EncryptedSessionKey = Convert.ToBase64String(encryptedSessionKey),
                    Iv = Convert.ToBase64String(iv),
                    PcId = "PC-FRAMING-TEST",
                    Hostname = "DESKTOP-FRAMING"
                };

                string responseJson = JsonSerializer.Serialize(responseDto) + "\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(responseJson));
                await stream.FlushAsync();

                // Read AUTH_STATUS (SUCCESS)
                string statusLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var statusDto = JsonSerializer.Deserialize<AuthStatusMessage>(statusLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Assert.NotNull(statusDto);
                Assert.Equal("SUCCESS", statusDto.Status);

                // 2. Now client is authenticated. Send oversized message (exceeding 200 bytes limit)
                byte[] oversizedData = new byte[300];
                Array.Fill(oversizedData, (byte)'X');
                oversizedData[299] = (byte)'\n';

                await stream.WriteAsync(oversizedData, 0, oversizedData.Length);
                await stream.FlushAsync();

                // Server should detect FrameTooLarge on connection.Reader, log gracefully, and close connection
                await Task.Delay(200);

                // Assert connection closed by reading EOF (returns 0)
                byte[] readBuf = new byte[1];
                int bytesRead = await stream.ReadAsync(readBuf, 0, 1);
                Assert.Equal(0, bytesRead); // EOF
            }
            finally
            {
                client.Close();
                await Task.Delay(100);
                await server.StopAsync(CancellationToken.None);
            }
        }
    }
}

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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Sayra.Backend.Api;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Infrastructure.Transport;

namespace Sayra.Backend.IntegrationTests
{
    public class WorkstationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly ITcpAuthenticationService _authService;
        private readonly ICryptographicService _cryptoService;
        private readonly IRedisService _redisService;
        private readonly ITcpConnectionRegistry _connectionRegistry;
        private readonly ISecureMessageService _secureMessageService;
        private readonly string _masterKey;

        public WorkstationIntegrationTests(WebApplicationFactory<Program> factory)
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

        #endregion

        #region DB Integration Tests

        [Fact]
        public async Task DbContext_Should_Enforce_Unique_PcId_Constraint()
        {
            using var scope = _factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var pcId = $"PC-UNIQUE-CONSTR-{Guid.NewGuid():N}";

            var w1 = new Workstation
            {
                PcId = pcId,
                SiteId = "SITE-ALPHA",
                Hostname = "DESKTOP-01",
                MacAddress = $"AA:BB:CC:DD:EE:{Random.Shared.Next(10, 99)}",
                IpAddress = "192.168.1.15",
                Status = "Offline",
                LastSeen = DateTime.UtcNow
            };

            var w2 = new Workstation
            {
                PcId = pcId, // DUPLICATE PC ID
                SiteId = "SITE-BETA",
                Hostname = "DESKTOP-02",
                MacAddress = $"AA:BB:CC:DD:EE:{Random.Shared.Next(10, 99)}",
                IpAddress = "192.168.1.16",
                Status = "Offline",
                LastSeen = DateTime.UtcNow
            };

            await dbContext.Workstations.AddAsync(w1);
            await dbContext.SaveChangesAsync();

            await dbContext.Workstations.AddAsync(w2);

            var exception = await Assert.ThrowsAsync<DbUpdateException>(async () =>
            {
                await dbContext.SaveChangesAsync();
            });

            Assert.NotNull(exception);
        }

        [Fact]
        public async Task DbContext_Should_Enforce_Unique_MacAddress_Constraint()
        {
            using var scope = _factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var mac = $"AA:BB:CC:DD:EE:{Random.Shared.Next(10, 99)}";

            var w1 = new Workstation
            {
                PcId = $"PC-MAC-1-{Guid.NewGuid():N}",
                SiteId = "SITE-ALPHA",
                Hostname = "DESKTOP-01",
                MacAddress = mac,
                IpAddress = "192.168.1.20",
                Status = "Offline",
                LastSeen = DateTime.UtcNow
            };

            var w2 = new Workstation
            {
                PcId = $"PC-MAC-2-{Guid.NewGuid():N}",
                SiteId = "SITE-BETA",
                Hostname = "DESKTOP-02",
                MacAddress = mac, // DUPLICATE MAC
                IpAddress = "192.168.1.21",
                Status = "Offline",
                LastSeen = DateTime.UtcNow
            };

            await dbContext.Workstations.AddAsync(w1);
            await dbContext.SaveChangesAsync();

            await dbContext.Workstations.AddAsync(w2);

            var exception = await Assert.ThrowsAsync<DbUpdateException>(async () =>
            {
                await dbContext.SaveChangesAsync();
            });

            Assert.NotNull(exception);
        }

        #endregion

        #region TCP & Identity Integration Tests

        [Fact]
        public async Task Registered_Device_Should_Authenticate_And_Bind_Successfully()
        {
            // 1. Arrange: Register device in DB
            using (var scope = _factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var pcId = "PC-INTEG-OK-01";
                var existing = await dbContext.Workstations.FirstOrDefaultAsync(w => w.PcId == pcId);
                if (existing == null)
                {
                    var workstation = new Workstation
                    {
                        PcId = pcId,
                        SiteId = "SITE-ALPHA",
                        Hostname = "HOST-OK",
                        MacAddress = "00:11:22:33:44:FF",
                        IpAddress = "127.0.0.1",
                        Status = "Offline",
                        IsDisabled = false
                    };
                    await dbContext.Workstations.AddAsync(workstation);
                    await dbContext.SaveChangesAsync();
                }
                else
                {
                    existing.IsDisabled = false;
                    existing.Status = "Offline";
                    await dbContext.SaveChangesAsync();
                }
            }

            var (server, port) = await StartTestServerAsync();
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            var stream = client.GetStream();

            try
            {
                // 2. Read challenge
                string challengeLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var challengeDto = JsonSerializer.Deserialize<AuthChallengeDto>(challengeLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                // 3. Build response
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
                    PcId = "PC-INTEG-OK-01",
                    Hostname = "HOST-OK",
                    SiteId = "SITE-ALPHA"
                };

                await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responseDto) + "\n"));
                await stream.FlushAsync();

                // 4. Read Status Success
                string statusLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var statusDto = JsonSerializer.Deserialize<AuthStatusDto>(statusLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Assert.Equal("SUCCESS", statusDto!.Status);

                // 5. Verify database status updated to Online
                using (var scope = _factory.Services.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var workstation = await dbContext.Workstations.AsNoTracking().FirstOrDefaultAsync(w => w.PcId == "PC-INTEG-OK-01");
                    Assert.NotNull(workstation);
                    Assert.Equal("ONLINE", workstation.Status, ignoreCase: true);
                }
            }
            finally
            {
                client.Close();
                await Task.Delay(150);
                await server.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task Unregistered_Device_Should_Fail_With_DEVICE_NOT_REGISTERED()
        {
            var (server, port) = await StartTestServerAsync();
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            var stream = client.GetStream();

            try
            {
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
                    PcId = "PC-NOT-REGISTERED-99", // UNKNOWN PC ID
                    Hostname = "HOST-UNKNOWN"
                };

                await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responseDto) + "\n"));
                await stream.FlushAsync();

                // Read Status: should return FAILED with DEVICE_NOT_REGISTERED
                string statusLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var statusDto = JsonSerializer.Deserialize<AuthStatusDto>(statusLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Assert.Equal("FAILED", statusDto!.Status);
                Assert.Equal("DEVICE_NOT_REGISTERED", statusDto.ErrorCode);

                // Verify connection immediately closes
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
        public async Task Disabled_Device_Should_Fail_With_AUTH_FAILED()
        {
            // 1. Arrange: Register a disabled device
            using (var scope = _factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var pcId = "PC-INTEG-DISABLED";
                var existing = await dbContext.Workstations.FirstOrDefaultAsync(w => w.PcId == pcId);
                if (existing == null)
                {
                    var workstation = new Workstation
                    {
                        PcId = pcId,
                        SiteId = "SITE-ALPHA",
                        Hostname = "HOST-DISABLED",
                        MacAddress = "00:11:22:33:44:EE",
                        IpAddress = "127.0.0.1",
                        Status = "Offline",
                        IsDisabled = true // DISABLED DEVICE
                    };
                    await dbContext.Workstations.AddAsync(workstation);
                }
                else
                {
                    existing.IsDisabled = true;
                }
                await dbContext.SaveChangesAsync();
            }

            var (server, port) = await StartTestServerAsync();
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            var stream = client.GetStream();

            try
            {
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
                    PcId = "PC-INTEG-DISABLED",
                    Hostname = "HOST-DISABLED"
                };

                await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responseDto) + "\n"));
                await stream.FlushAsync();

                // Read Status: should return FAILED with AUTH_FAILED
                string statusLine = await ReadLineWithTimeoutAsync(stream, TimeSpan.FromSeconds(3));
                var statusDto = JsonSerializer.Deserialize<AuthStatusDto>(statusLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Assert.Equal("FAILED", statusDto!.Status);
                Assert.Equal("AUTH_FAILED", statusDto.ErrorCode);

                // Verify connection immediately closes
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
        public async Task Second_Connection_With_Same_PcId_Should_Replace_Existing_Connection()
        {
            // 1. Arrange: Register device
            using (var scope = _factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var pcId = "PC-CONCURRENT-01";
                var existing = await dbContext.Workstations.FirstOrDefaultAsync(w => w.PcId == pcId);
                if (existing == null)
                {
                    var workstation = new Workstation
                    {
                        PcId = pcId,
                        SiteId = "SITE-ALPHA",
                        Hostname = "HOST-CONC",
                        MacAddress = "00:11:22:33:44:CC",
                        IpAddress = "127.0.0.1",
                        Status = "Offline",
                        IsDisabled = false
                    };
                    await dbContext.Workstations.AddAsync(workstation);
                    await dbContext.SaveChangesAsync();
                }
            }

            var (server, port) = await StartTestServerAsync();

            // Client 1 Connection
            using var client1 = new TcpClient();
            await client1.ConnectAsync("127.0.0.1", port);
            var stream1 = client1.GetStream();

            // Client 2 Connection
            using var client2 = new TcpClient();

            try
            {
                // Authenticate Client 1
                string chal1 = await ReadLineWithTimeoutAsync(stream1, TimeSpan.FromSeconds(3));
                var challengeDto1 = JsonSerializer.Deserialize<AuthChallengeDto>(chal1, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                byte[] masterKeyBytes = Encoding.UTF8.GetBytes(_masterKey);
                byte[] chalBytes1 = Encoding.UTF8.GetBytes(challengeDto1!.Challenge);
                byte[] hmac1 = _cryptoService.ComputeHmacSha256(chalBytes1, masterKeyBytes);
                byte[] sk1 = RandomNumberGenerator.GetBytes(32);
                byte[] iv1 = RandomNumberGenerator.GetBytes(16);
                byte[] aesKey = masterKeyBytes.Length == 32 ? masterKeyBytes : SHA256.HashData(masterKeyBytes);
                byte[] encryptedSk1 = _cryptoService.EncryptAes256Cbc(sk1, aesKey, iv1);

                var response1 = new AuthResponseDto
                {
                    Hmac = Convert.ToBase64String(hmac1),
                    EncryptedSessionKey = Convert.ToBase64String(encryptedSk1),
                    Iv = Convert.ToBase64String(iv1),
                    PcId = "PC-CONCURRENT-01",
                    Hostname = "HOST-CONC"
                };
                await stream1.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response1) + "\n"));
                await stream1.FlushAsync();

                // Read success status for Client 1
                await ReadLineWithTimeoutAsync(stream1, TimeSpan.FromSeconds(3));

                // Verify Client 1 is in active registry
                Assert.Equal(1, _connectionRegistry.Count);

                // Now connect Client 2 with identical PC-ID
                await client2.ConnectAsync("127.0.0.1", port);
                var stream2 = client2.GetStream();

                string chal2 = await ReadLineWithTimeoutAsync(stream2, TimeSpan.FromSeconds(3));
                var challengeDto2 = JsonSerializer.Deserialize<AuthChallengeDto>(chal2, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                byte[] chalBytes2 = Encoding.UTF8.GetBytes(challengeDto2!.Challenge);
                byte[] hmac2 = _cryptoService.ComputeHmacSha256(chalBytes2, masterKeyBytes);
                byte[] sk2 = RandomNumberGenerator.GetBytes(32);
                byte[] iv2 = RandomNumberGenerator.GetBytes(16);
                byte[] encryptedSk2 = _cryptoService.EncryptAes256Cbc(sk2, aesKey, iv2);

                var response2 = new AuthResponseDto
                {
                    Hmac = Convert.ToBase64String(hmac2),
                    EncryptedSessionKey = Convert.ToBase64String(encryptedSk2),
                    Iv = Convert.ToBase64String(iv2),
                    PcId = "PC-CONCURRENT-01", // IDENTICAL
                    Hostname = "HOST-CONC"
                };
                await stream2.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response2) + "\n"));
                await stream2.FlushAsync();

                // Read success status for Client 2
                string statusLine2 = await ReadLineWithTimeoutAsync(stream2, TimeSpan.FromSeconds(3));
                var statusDto2 = JsonSerializer.Deserialize<AuthStatusDto>(statusLine2, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Assert.Equal("SUCCESS", statusDto2!.Status);

                // Give server a bit of time to unregister and close old connection
                await Task.Delay(100);

                // Verify old Client 1 is disconnected
                int read1 = await stream1.ReadAsync(new byte[1], 0, 1);
                Assert.Equal(0, read1); // EOF (closed by server)

                // Verify registry holds Client 2 and size is still exactly 1
                Assert.Equal(1, _connectionRegistry.Count);
                var activeConn = _connectionRegistry.GetAll().First();
                Assert.Equal("PC-CONCURRENT-01", activeConn.PcId);
                Assert.True(CryptographicOperations.FixedTimeEquals(sk2, activeConn.SessionKey!));
            }
            finally
            {
                client1.Close();
                client2.Close();
                await Task.Delay(100);
                await server.StopAsync(CancellationToken.None);
            }
        }

        #endregion
    }
}

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Security;
using Sayra.Backend.Infrastructure.Transport;

namespace Sayra.Backend.IntegrationTests
{
    public class TcpServerTransportIntegrationTests
    {
        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        [Fact]
        public async Task TcpServer_RejectsConnections_When_MaximumConnectionsLimitReached()
        {
            int freePort = GetFreePort();
            var registry = new TcpConnectionRegistry();
            var serverOpts = Options.Create(new ServerOptions
            {
                Port = freePort,
                Host = "127.0.0.1",
                MaximumConnections = 2
            });
            var tlsOpts = Options.Create(new TlsOptions());
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var serverLogger = loggerFactory.CreateLogger<TcpServer>();

            var mockAuthService = new TransportAndPipelineTests.MockTcpAuthenticationService();
            var mockCryptoService = new CryptographicService();
            var mockRedisService = new TransportAndPipelineTests.MockRedisService();
            var mockSecureMsgService = new SecureMessageService(mockCryptoService, registry, loggerFactory.CreateLogger<SecureMessageService>());

            using var server = new TcpServer(registry, mockAuthService, mockCryptoService, mockRedisService, mockSecureMsgService, serverOpts, tlsOpts, serverLogger);
            await server.StartAsync(CancellationToken.None);

            using var client1 = new TcpClient();
            await client1.ConnectAsync("127.0.0.1", freePort);
            using var client2 = new TcpClient();
            await client2.ConnectAsync("127.0.0.1", freePort);

            await Task.Delay(150);
            Assert.Equal(2, registry.Count);

            // Third connection should be rejected by server
            using var client3 = new TcpClient();
            await client3.ConnectAsync("127.0.0.1", freePort);
            await Task.Delay(150);

            // Connection registry should still be 2
            Assert.Equal(2, registry.Count);

            await server.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task TcpServer_Handles_ConcurrentClients_And_GracefulDisconnects()
        {
            int freePort = GetFreePort();
            var registry = new TcpConnectionRegistry();
            var serverOpts = Options.Create(new ServerOptions
            {
                Port = freePort,
                Host = "127.0.0.1",
                MaximumConnections = 50
            });
            var tlsOpts = Options.Create(new TlsOptions());
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var serverLogger = loggerFactory.CreateLogger<TcpServer>();

            var mockAuthService = new TransportAndPipelineTests.MockTcpAuthenticationService();
            var mockCryptoService = new CryptographicService();
            var mockRedisService = new TransportAndPipelineTests.MockRedisService();
            var mockSecureMsgService = new SecureMessageService(mockCryptoService, registry, loggerFactory.CreateLogger<SecureMessageService>());

            using var server = new TcpServer(registry, mockAuthService, mockCryptoService, mockRedisService, mockSecureMsgService, serverOpts, tlsOpts, serverLogger);
            await server.StartAsync(CancellationToken.None);

            int clientCount = 20;
            var clients = new TcpClient[clientCount];

            for (int i = 0; i < clientCount; i++)
            {
                clients[i] = new TcpClient();
                await clients[i].ConnectAsync("127.0.0.1", freePort);
            }

            await Task.Delay(200);
            Assert.Equal(clientCount, registry.Count);

            // Disconnect half of the clients
            for (int i = 0; i < clientCount / 2; i++)
            {
                clients[i].Close();
                clients[i].Dispose();
            }

            await Task.Delay(200);
            Assert.Equal(clientCount / 2, registry.Count);

            // Stop server
            await server.StopAsync(CancellationToken.None);
            Assert.Equal(0, registry.Count);

            for (int i = clientCount / 2; i < clientCount; i++)
            {
                clients[i].Dispose();
            }
        }
    }
}

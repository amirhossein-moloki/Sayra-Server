using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Sayra.Backend.Api;
using Sayra.Backend.Api.Middleware;
using Sayra.Backend.Api.Models;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Infrastructure.Configuration;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Transport;

namespace Sayra.Backend.IntegrationTests
{
    public class TransportAndPipelineTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public TransportAndPipelineTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        #region HTTP Pipeline & Exception Mapping Tests

        [Fact]
        public async Task Health_Endpoints_Should_Respond_Correctly()
        {
            var client = _factory.CreateClient();

            // Liveness
            var liveResponse = await client.GetAsync("/api/health/live");
            Assert.True(liveResponse.IsSuccessStatusCode);
            var liveContent = await liveResponse.Content.ReadAsStringAsync();
            Assert.Contains("Alive", liveContent);

            // Health (PostgreSQL and Redis are up in our sandbox)
            var healthResponse = await client.GetAsync("/api/health");
            Assert.True(healthResponse.IsSuccessStatusCode);
        }

        [Fact]
        public async Task Pipeline_Should_Propagate_CorrelationId()
        {
            var client = _factory.CreateClient();
            var customCorrelationId = Guid.NewGuid().ToString();

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/health/live");
            request.Headers.Add("X-Correlation-ID", customCorrelationId);

            var response = await client.SendAsync(request);
            Assert.True(response.IsSuccessStatusCode);

            // Assert response header contains propagation
            Assert.True(response.Headers.Contains("X-Correlation-ID"));
            var returnedCorrelationId = response.Headers.GetValues("X-Correlation-ID");
            Assert.Contains(customCorrelationId, returnedCorrelationId);
        }

        [Fact]
        public async Task Pipeline_Should_Generate_CorrelationId_If_Not_Provided()
        {
            var client = _factory.CreateClient();
            var response = await client.GetAsync("/api/health/live");

            Assert.True(response.Headers.Contains("X-Correlation-ID"));
            var returnedCorrelationId = string.Join("", response.Headers.GetValues("X-Correlation-ID"));
            Assert.False(string.IsNullOrWhiteSpace(returnedCorrelationId));
        }

        [Theory]
        [InlineData(typeof(AuthFailedException), "AUTH_FAILED", 401)]
        [InlineData(typeof(DeviceNotRegisteredException), "DEVICE_NOT_REGISTERED", 403)]
        [InlineData(typeof(InvalidCommandException), "INVALID_COMMAND", 400)]
        [InlineData(typeof(SessionExpiredException), "SESSION_EXPIRED", 401)]
        public async Task ExceptionHandlingMiddleware_Should_Map_DomainExceptions_To_Correct_ErrorContracts(
            Type exceptionType, string expectedCode, int expectedStatusCode)
        {
            // Arrange
            var logger = LoggerFactory.Create(builder => builder.AddConsole())
                .CreateLogger<ExceptionHandlingMiddleware>();

            var middleware = new ExceptionHandlingMiddleware(context =>
            {
                var ex = (Exception)Activator.CreateInstance(exceptionType, "Test message")!;
                throw ex;
            }, logger);

            var httpContext = new DefaultHttpContext();
            httpContext.Response.Body = new MemoryStream();

            // Act
            await middleware.InvokeAsync(httpContext);

            // Assert
            Assert.Equal(expectedStatusCode, httpContext.Response.StatusCode);
            Assert.Equal("application/json", httpContext.Response.ContentType);

            httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(httpContext.Response.Body);
            string responseText = await reader.ReadToEndAsync();

            var errorResponse = System.Text.Json.JsonSerializer.Deserialize<ErrorResponse>(responseText, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

            Assert.NotNull(errorResponse);
            Assert.Equal(expectedCode, errorResponse.Code);
            Assert.Equal("Test message", errorResponse.Title);
            Assert.Equal(expectedStatusCode, errorResponse.Status);
            Assert.False(string.IsNullOrEmpty(errorResponse.TraceId));
        }

        #endregion

        #region Configuration Validation Tests

        [Fact]
        public void ConfigurationValidator_Should_Pass_For_Valid_Config()
        {
            var db = new DatabaseOptions { ConnectionString = "Host=localhost;Database=sayra_db;Username=postgres" };
            var redis = new RedisOptions { ConnectionString = "localhost:6379" };
            var server = new ServerOptions { Port = 5000 };
            var discovery = new DiscoveryOptions { UdpPort = 5001 };

            // Should not throw
            ConfigurationValidator.Validate(db, redis, server, discovery);
        }

        [Fact]
        public void ConfigurationValidator_Should_Fail_Fast_For_Missing_Database_Config()
        {
            var db = new DatabaseOptions { ConnectionString = "" };
            var redis = new RedisOptions { ConnectionString = "localhost:6379" };
            var server = new ServerOptions { Port = 5000 };
            var discovery = new DiscoveryOptions { UdpPort = 5001 };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ConfigurationValidator.Validate(db, redis, server, discovery));

            Assert.Contains("Database:ConnectionString", exception.Message);
        }

        [Fact]
        public void ConfigurationValidator_Should_Fail_Fast_For_Missing_Redis_Config()
        {
            var db = new DatabaseOptions { ConnectionString = "Host=localhost;Database=sayra_db" };
            var redis = new RedisOptions { ConnectionString = "" };
            var server = new ServerOptions { Port = 5000 };
            var discovery = new DiscoveryOptions { UdpPort = 5001 };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ConfigurationValidator.Validate(db, redis, server, discovery));

            Assert.Contains("Redis:ConnectionString", exception.Message);
        }

        [Fact]
        public void ConfigurationValidator_Should_Fail_Fast_For_Invalid_Server_Port()
        {
            var db = new DatabaseOptions { ConnectionString = "Host=localhost" };
            var redis = new RedisOptions { ConnectionString = "localhost" };
            var server = new ServerOptions { Port = 99999 }; // Out of range
            var discovery = new DiscoveryOptions { UdpPort = 5001 };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ConfigurationValidator.Validate(db, redis, server, discovery));

            Assert.Contains("Server:Port", exception.Message);
        }

        [Fact]
        public void ConfigurationValidator_Should_Fail_Fast_For_Invalid_Discovery_Port()
        {
            var db = new DatabaseOptions { ConnectionString = "Host=localhost" };
            var redis = new RedisOptions { ConnectionString = "localhost" };
            var server = new ServerOptions { Port = 5000 };
            var discovery = new DiscoveryOptions { UdpPort = 0 }; // Out of range

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ConfigurationValidator.Validate(db, redis, server, discovery));

            Assert.Contains("Discovery:UdpPort", exception.Message);
        }

        #endregion

        #region TCP Transport Foundation Tests

        [Fact]
        public async Task TcpServer_Should_Accept_And_Track_Connections_And_Shutdown_Cleanly()
        {
            int freePort = GetFreePort();
            var registry = new TcpConnectionRegistry();
            var serverOpts = Options.Create(new ServerOptions { Port = freePort, Host = "127.0.0.1" });
            var tlsOpts = Options.Create(new TlsOptions());
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var serverLogger = loggerFactory.CreateLogger<TcpServer>();

            using var server = new TcpServer(registry, serverOpts, tlsOpts, serverLogger);

            // Start Server
            await server.StartAsync(CancellationToken.None);

            // Connect Client
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", freePort);

            // Wait brief moment for the accepted task to run
            await Task.Delay(100);

            // Verify connection was accepted and registered
            Assert.Equal(1, registry.Count);

            // Verify registry retrieval
            var connections = registry.GetAll();
            ITcpConnection? connection = null;
            foreach (var conn in connections)
            {
                connection = conn;
                break;
            }

            Assert.NotNull(connection);
            Assert.False(string.IsNullOrEmpty(connection.ConnectionId));
            Assert.Equal(ConnectionLifecycleState.Active, connection.State);

            // Disconnect Client
            client.Close();
            await Task.Delay(100);

            // Registry should be cleared after disconnect
            Assert.Equal(0, registry.Count);

            // Stop Server
            await server.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task TcpServer_Graceful_Shutdown_Should_Close_Active_Connections()
        {
            int freePort = GetFreePort();
            var registry = new TcpConnectionRegistry();
            var serverOpts = Options.Create(new ServerOptions { Port = freePort, Host = "127.0.0.1" });
            var tlsOpts = Options.Create(new TlsOptions());
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var serverLogger = loggerFactory.CreateLogger<TcpServer>();

            using var server = new TcpServer(registry, serverOpts, tlsOpts, serverLogger);

            await server.StartAsync(CancellationToken.None);

            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", freePort);
            await Task.Delay(100);

            Assert.Equal(1, registry.Count);

            // Stop server while connection is active
            await server.StopAsync(CancellationToken.None);

            // Active connections should be cleared and closed
            Assert.Equal(0, registry.Count);
        }

        #endregion

        #region UDP Discovery Foundation Tests

        [Fact]
        public async Task UdpDiscoveryServer_Should_Listen_And_Shutdown_Cleanly()
        {
            int freeUdpPort = GetFreePort();
            var discoveryOpts = Options.Create(new DiscoveryOptions { Enabled = true, UdpPort = freeUdpPort });
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var udpLogger = loggerFactory.CreateLogger<UdpDiscoveryServer>();

            using var discoveryServer = new UdpDiscoveryServer(discoveryOpts, udpLogger);

            // Start UDP listener
            await discoveryServer.StartAsync(CancellationToken.None);

            // Send UDP datagram to the port
            using var udpClient = new UdpClient();
            byte[] payload = Encoding.UTF8.GetBytes("DISCOVER_SAYRA_SERVER");
            await udpClient.SendAsync(payload, payload.Length, "127.0.0.1", freeUdpPort);

            await Task.Delay(100);

            // Send malformed payload to verify resilience (no crashes)
            byte[] malformed = new byte[] { 0x00, 0xFF, 0x12, 0x99 };
            await udpClient.SendAsync(malformed, malformed.Length, "127.0.0.1", freeUdpPort);

            await Task.Delay(100);

            // Stop UDP listener
            await discoveryServer.StopAsync(CancellationToken.None);
        }

        #endregion

        #region Helper methods

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        #endregion
    }
}

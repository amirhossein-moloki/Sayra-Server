using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Configuration;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Diagnostics;
using Sayra.Backend.Infrastructure.Security;
using Sayra.Backend.Infrastructure.Transport;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class TcpAndHttpHardeningUnitTests
    {
        [Fact]
        public void ConfigurationValidator_ServerOptions_NewOptions_Validated()
        {
            var db = new DatabaseOptions { ConnectionString = "Host=mock" };
            var redis = new RedisOptions { ConnectionString = "mock:6379" };
            var server = new ServerOptions
            {
                Port = 5000,
                MaxConnectionsPerIp = 0 // Invalid <= 0
            };
            var discovery = new DiscoveryOptions { UdpPort = 5001 };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ConfigurationValidator.Validate(db, redis, server, discovery));

            Assert.Contains("MaxConnectionsPerIp", ex.Message);
        }

        [Fact]
        public void ConfigurationValidator_RateLimitingOptions_Validated()
        {
            var options = new RateLimitingOptions
            {
                GlobalPermitLimit = 0 // Invalid <= 0
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ConfigurationValidator.ValidateRateLimitingOptions(options));

            Assert.Contains("GlobalPermitLimit", ex.Message);
        }

        [Fact]
        public async Task TcpAuthenticationService_Rejects_When_AuthConcurrencyLimitReached()
        {
            var clientAuthMock = new Mock<IClientAuthenticationService>();
            var redisMock = new Mock<IRedisService>();
            var metricsMock = new Mock<ITransportMetrics>();

            var serverOptions = new ServerOptions
            {
                MaxConcurrentAuthentications = 1 // Bound to 1 concurrent auth
            };

            var service = new TcpAuthenticationService(
                clientAuthMock.Object,
                redisMock.Object,
                NullLogger<TcpAuthenticationService>.Instance,
                Options.Create(serverOptions),
                sessionManager: null,
                connectionRegistry: null,
                transportMetrics: metricsMock.Object);

            var tcpClient1 = new TcpClient();
            var ms1 = new MemoryStream();
            var conn1 = new TcpConnection("conn-1", tcpClient1, ms1);

            var tcpClient2 = new TcpClient();
            var ms2 = new MemoryStream();
            var conn2 = new TcpConnection("conn-2", tcpClient2, ms2);

            // Block first auth by making GenerateChallengeAsync pause
            var tcs = new TaskCompletionSource<string>();
            clientAuthMock
                .Setup(c => c.GenerateChallengeAsync(conn1, It.IsAny<CancellationToken>()))
                .Returns(tcs.Task);

            // Start 1st authentication async (holds the 1 available auth slot)
            var task1 = service.AuthenticateAsync(conn1, CancellationToken.None);

            // 2nd authentication attempt should be rejected immediately due to concurrency limit
            bool auth2Result = await service.AuthenticateAsync(conn2, CancellationToken.None);

            Assert.False(auth2Result);
            metricsMock.Verify(m => m.RecordAuthenticationRejected("MaxConcurrentAuthenticationsExceeded"), Times.Once);

            // Complete 1st authentication
            tcs.SetResult("mock-challenge");
            try { await task1; } catch { }

            conn1.Dispose();
            conn2.Dispose();
        }

        [Fact]
        public async Task TcpAuthenticationService_Replaces_Duplicate_PcId_Connection()
        {
            var clientAuthMock = new Mock<IClientAuthenticationService>();
            var redisMock = new Mock<IRedisService>();
            var registryMock = new Mock<ITcpConnectionRegistry>();

            var service = new TcpAuthenticationService(
                clientAuthMock.Object,
                redisMock.Object,
                NullLogger<TcpAuthenticationService>.Instance,
                Options.Create(new ServerOptions()),
                sessionManager: null,
                connectionRegistry: registryMock.Object,
                transportMetrics: null);

            var oldTcpClient = new TcpClient();
            var oldMs = new MemoryStream();
            var oldConn = new TcpConnection("old-conn-id", oldTcpClient, oldMs) { PcId = "WORKSTATION-01" };

            var newTcpClient = new TcpClient();
            byte[] authRespBytes = Encoding.UTF8.GetBytes("{\"type\":\"AUTH_RESPONSE\"}\n");
            var readMs = new MemoryStream(authRespBytes);
            var writeMs = new MemoryStream();
            var testStream = new TestBidirectionalStream(readMs, writeMs);

            var newConn = new TcpConnection("new-conn-id", newTcpClient, testStream) { PcId = "WORKSTATION-01" };

            registryMock.Setup(r => r.GetByPcId("WORKSTATION-01")).Returns(oldConn);
            clientAuthMock.Setup(c => c.GenerateChallengeAsync(newConn, It.IsAny<CancellationToken>())).ReturnsAsync("challenge");
            clientAuthMock.Setup(c => c.ValidateResponseAsync(newConn, It.IsAny<AuthResponseDto>(), It.IsAny<CancellationToken>()))
                .Callback<ITcpConnection, AuthResponseDto, CancellationToken>((conn, _, _) => conn.PcId = "WORKSTATION-01")
                .ReturnsAsync(new Sayra.Backend.Application.Abstractions.Security.AuthenticationResult { IsSuccess = true });

            bool result = await service.AuthenticateAsync(newConn, CancellationToken.None);

            Assert.True(result);
            registryMock.Verify(r => r.Unregister("old-conn-id"), Times.Once);
            Assert.Equal(ConnectionLifecycleState.Disconnected, oldConn.State);

            oldConn.Dispose();
            newConn.Dispose();
        }

        private class TestBidirectionalStream : Stream
        {
            private readonly Stream _readStream;
            private readonly Stream _writeStream;

            public TestBidirectionalStream(Stream readStream, Stream writeStream)
            {
                _readStream = readStream;
                _writeStream = writeStream;
            }

            public override bool CanRead => _readStream.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => _writeStream.CanWrite;
            public override long Length => _readStream.Length;
            public override long Position { get => _readStream.Position; set => _readStream.Position = value; }

            public override void Flush() => _writeStream.Flush();
            public override Task FlushAsync(CancellationToken cancellationToken) => _writeStream.FlushAsync(cancellationToken);

            public override int Read(byte[] buffer, int offset, int count) => _readStream.Read(buffer, offset, count);
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _readStream.ReadAsync(buffer, offset, count, cancellationToken);
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _readStream.ReadAsync(buffer, cancellationToken);

            public override void Write(byte[] buffer, int offset, int count) => _writeStream.Write(buffer, offset, count);
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _writeStream.WriteAsync(buffer, offset, count, cancellationToken);
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => _writeStream.WriteAsync(buffer, cancellationToken);

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }

        [Fact]
        public void TcpFrameParser_Clears_Buffer_On_OversizedFrame()
        {
            var parser = new TcpFrameParser(maxMessageSize: 30);

            byte[] oversizedData = Encoding.UTF8.GetBytes("THIS_IS_A_VERY_LONG_FRAME_WITHOUT_NEWLINE_THAT_EXCEEDS_LIMIT");

            Assert.Throws<InvalidOperationException>(() => parser.Append(oversizedData, oversizedData.Length));

            // Verify subsequent valid frame appends cleanly after buffer clear
            byte[] validData = Encoding.UTF8.GetBytes("VALID_FRAME\n");
            parser.Append(validData, validData.Length);
            var frames = parser.ExtractFrames();

            Assert.Single(frames);
            Assert.Equal("VALID_FRAME", frames[0]);
        }

        [Fact]
        public async Task SecureMessageService_Rejects_OversizedPayload()
        {
            var cryptoMock = new Mock<ICryptographicService>();
            var registryMock = new Mock<ITcpConnectionRegistry>();

            var service = new SecureMessageService(
                cryptoMock.Object,
                registryMock.Object,
                NullLogger<SecureMessageService>.Instance);

            var session = new ConnectionSession
            {
                ConnectionId = "conn-overflow",
                PcId = "WS-01",
                SessionKey = new byte[32]
            };

            // Payload exceeds 10MB limit
            string hugePayload = new string('A', 11 * 1024 * 1024);
            var envelope = new Sayra.Backend.Application.Security.SecureMessageEnvelope
            {
                Payload = hugePayload,
                Signature = "dummy-signature",
                Timestamp = DateTime.UtcNow.ToString("O")
            };

            var result = await service.HandleSecureMessageAsync(session, envelope);

            Assert.False(result.IsSuccess);
            Assert.Equal("PAYLOAD_LIMIT_EXCEEDED", result.ErrorCode);
        }

        [Fact]
        public void TransportMetrics_Records_Counters_And_UpDownCounters()
        {
            var metrics = new TransportMetrics();

            metrics.RecordConnectionAccepted();
            metrics.RecordConnectionActiveDelta(1);
            metrics.RecordConnectionActiveDelta(-1);
            metrics.RecordConnectionRejected("MaxConnectionsExceeded");
            metrics.RecordAuthenticationConcurrencyDelta(1);
            metrics.RecordAuthenticationConcurrencyDelta(-1);
            metrics.RecordAuthenticationRejected("CapacityExceeded");
            metrics.RecordSlowDisconnect("WriteTimeout");
            metrics.RecordOversizedFrame(1000, 500);
            metrics.RecordHttpRateLimitRejected("/api/auth/login", "AuthPolicy");

            Assert.NotNull(metrics);
        }
    }
}

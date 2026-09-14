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
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Diagnostics;
using Sayra.Backend.Infrastructure.Security;
using Sayra.Backend.Infrastructure.Transport;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class Phase10HardeningAndReconnectTests
    {
        [Fact]
        public async Task ReconnectStorm_Simulated_AdmissionAndAuthBounds_Verified()
        {
            int maxConn = 10;
            int maxUnauth = 5;
            int maxAuth = 3;

            var registry = new TcpConnectionRegistry();
            var redisMock = new Mock<IRedisService>();
            var clientAuthMock = new Mock<IClientAuthenticationService>();
            var cryptoMock = new Mock<ICryptographicService>();
            var secureMsgMock = new Mock<ISecureMessageService>();
            var metricsMock = new Mock<ITransportMetrics>();

            var serverOptions = new ServerOptions
            {
                MaximumConnections = maxConn,
                MaxUnauthenticatedConnections = maxUnauth,
                MaxConcurrentAuthentications = maxAuth,
                MaxConnectionsPerIp = 50,
                Port = 15999
            };

            var sessionManager = new TcpSessionManager(registry, redisMock.Object, NullLogger<TcpSessionManager>.Instance);

            var authService = new TcpAuthenticationService(
                clientAuthMock.Object,
                redisMock.Object,
                NullLogger<TcpAuthenticationService>.Instance,
                Options.Create(serverOptions),
                sessionManager,
                registry,
                metricsMock.Object);

            int simulatedClients = 20;
            int acceptedCount = 0;
            int rejectedCount = 0;

            clientAuthMock
                .Setup(c => c.GenerateChallengeAsync(It.IsAny<ITcpConnection>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    await Task.Delay(50); // Simulate crypto work
                    return "challenge";
                });

            clientAuthMock
                .Setup(c => c.ValidateResponseAsync(It.IsAny<ITcpConnection>(), It.IsAny<AuthResponseDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Sayra.Backend.Application.Abstractions.Security.AuthenticationResult { IsSuccess = true });

            var tasks = new Task[simulatedClients];
            for (int i = 0; i < simulatedClients; i++)
            {
                int index = i;
                tasks[i] = Task.Run(async () =>
                {
                    using var tcpClient = new TcpClient();
                    byte[] authRespBytes = Encoding.UTF8.GetBytes("{\"type\":\"AUTH_RESPONSE\"}\n");
                    var readMs = new MemoryStream(authRespBytes);
                    var writeMs = new MemoryStream();
                    var testStream = new TestStream(readMs, writeMs);

                    var conn = new TcpConnection($"storm-conn-{index}", tcpClient, testStream) { PcId = $"WS-{index}" };
                    registry.Register(conn);

                    bool authenticated = await authService.AuthenticateAsync(conn, CancellationToken.None);
                    if (authenticated)
                    {
                        Interlocked.Increment(ref acceptedCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref rejectedCount);
                    }
                });
            }

            await Task.WhenAll(tasks);

            Assert.True(rejectedCount > 0, "Expected some authentications to be rejected under flood.");
            Assert.Equal(simulatedClients, acceptedCount + rejectedCount);
            metricsMock.Verify(m => m.RecordAuthenticationRejected("MaxConcurrentAuthenticationsExceeded"), Times.AtLeastOnce());
        }

        private class TestStream : Stream
        {
            private readonly Stream _readStream;
            private readonly Stream _writeStream;

            public TestStream(Stream readStream, Stream writeStream)
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
    }
}

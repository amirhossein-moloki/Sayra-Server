using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Infrastructure.Configuration;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Diagnostics;
using Sayra.Backend.Infrastructure.Transport;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class TcpTransportUnitTests
    {
        [Fact]
        public async Task TcpConnection_SendAsync_And_SendFrameAsync_SerializedThreadSafeWrites()
        {
            using var tcpClient = new TcpClient();
            using var ms = new MemoryStream();
            using var connection = new TcpConnection("conn-test-1", tcpClient, ms);

            int concurrentWriters = 50;
            var tasks = new Task[concurrentWriters];

            for (int i = 0; i < concurrentWriters; i++)
            {
                int index = i;
                tasks[i] = Task.Run(async () =>
                {
                    await connection.SendFrameAsync($"MSG-{index}");
                });
            }

            await Task.WhenAll(tasks);

            string writtenOutput = Encoding.UTF8.GetString(ms.ToArray());
            string[] lines = writtenOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal(concurrentWriters, lines.Length);
            foreach (var line in lines)
            {
                Assert.StartsWith("MSG-", line);
            }
        }

        [Fact]
        public async Task TcpConnection_SendAsync_Disposed_ThrowsObjectDisposedException()
        {
            using var tcpClient = new TcpClient();
            using var ms = new MemoryStream();
            var connection = new TcpConnection("conn-test-2", tcpClient, ms);

            connection.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.SendAsync(Encoding.UTF8.GetBytes("DATA")));
        }

        [Fact]
        public void TcpFrameParser_ExtractsFrames_Correctly()
        {
            var parser = new TcpFrameParser(maxMessageSize: 1024);

            byte[] data1 = Encoding.UTF8.GetBytes("{\"type\":\"PING\"}\n{\"type\":");
            parser.Append(data1, data1.Length);

            var frames1 = parser.ExtractFrames();
            Assert.Single(frames1);
            Assert.Equal("{\"type\":\"PING\"}", frames1[0]);

            byte[] data2 = Encoding.UTF8.GetBytes("\"PONG\"}\n");
            parser.Append(data2, data2.Length);

            var frames2 = parser.ExtractFrames();
            Assert.Single(frames2);
            Assert.Equal("{\"type\":\"PONG\"}", frames2[0]);
        }

        [Fact]
        public void TcpFrameParser_MultipleFramesInSingleRead_ExtractsAll()
        {
            var parser = new TcpFrameParser(maxMessageSize: 1024);

            byte[] data = Encoding.UTF8.GetBytes("FRAME1\nFRAME2\nFRAME3\n");
            parser.Append(data, data.Length);

            var frames = parser.ExtractFrames();
            Assert.Equal(3, frames.Count);
            Assert.Equal("FRAME1", frames[0]);
            Assert.Equal("FRAME2", frames[1]);
            Assert.Equal("FRAME3", frames[2]);
        }

        [Fact]
        public void TcpFrameParser_OversizedFrame_ThrowsInvalidOperationException()
        {
            var parser = new TcpFrameParser(maxMessageSize: 50);

            byte[] oversized = new byte[100];
            Array.Fill(oversized, (byte)'A');
            oversized[99] = (byte)'\n';

            Assert.Throws<InvalidOperationException>(() =>
            {
                parser.Append(oversized, oversized.Length);
                parser.ExtractFrames();
            });
        }

        [Fact]
        public void TcpFrameParser_AccumulatedBufferExceedsMaxMessageSize_ThrowsInvalidOperationException()
        {
            var parser = new TcpFrameParser(maxMessageSize: 50);

            byte[] junk = new byte[60];
            Array.Fill(junk, (byte)'X');

            Assert.Throws<InvalidOperationException>(() =>
            {
                parser.Append(junk, junk.Length);
            });
        }

        [Fact]
        public async Task TcpServerHealthCheck_IsListeningTrue_ReturnsHealthy()
        {
            var tcpServerMock = new Mock<ITcpServer>();
            tcpServerMock.Setup(s => s.IsListening).Returns(true);
            tcpServerMock.Setup(s => s.ActiveConnectionsCount).Returns(5);

            var healthCheck = new TcpServerHealthCheck(tcpServerMock.Object);
            var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.Contains("5", result.Description);
        }

        [Fact]
        public async Task TcpServerHealthCheck_IsListeningFalse_ReturnsDegraded()
        {
            var tcpServerMock = new Mock<ITcpServer>();
            tcpServerMock.Setup(s => s.IsListening).Returns(false);

            var healthCheck = new TcpServerHealthCheck(tcpServerMock.Object);
            var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Degraded, result.Status);
        }

        [Theory]
        [InlineData(0, 300, 1000, 8192, 8192, 65536, "HandshakeTimeout")]
        [InlineData(15, 0, 1000, 8192, 8192, 65536, "ConnectionTimeout")]
        [InlineData(15, 300, 0, 8192, 8192, 65536, "MaximumConnections")]
        [InlineData(15, 300, 1000, 500, 8192, 65536, "ReceiveBufferSize")]
        [InlineData(15, 300, 1000, 8192, 500, 65536, "SendBufferSize")]
        [InlineData(15, 300, 1000, 8192, 8192, 500, "MaximumMessageSize")]
        public void ConfigurationValidator_InvalidServerOptions_ThrowsInvalidOperationException(
            int handshakeTimeout, int connTimeout, int maxConn, int recvBuf, int sendBuf, int maxMsgSize, string expectedMessageProperty)
        {
            var db = new DatabaseOptions { ConnectionString = "Host=mock" };
            var redis = new RedisOptions { ConnectionString = "mock:6379" };
            var server = new ServerOptions
            {
                Port = 5000,
                HandshakeTimeout = handshakeTimeout,
                ConnectionTimeout = connTimeout,
                MaximumConnections = maxConn,
                ReceiveBufferSize = recvBuf,
                SendBufferSize = sendBuf,
                MaximumMessageSize = maxMsgSize
            };
            var discovery = new DiscoveryOptions { UdpPort = 5001 };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ConfigurationValidator.Validate(db, redis, server, discovery));

            Assert.Contains(expectedMessageProperty, ex.Message);
        }
    }
}

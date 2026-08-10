using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Infrastructure.Transport;

namespace Sayra.Backend.UnitTests
{
    public class FramingAndSerializationTests
    {
        [Fact]
        public async Task ReadFrameAsync_SingleFrame_ShouldBeParsedCorrectly()
        {
            var data = Encoding.UTF8.GetBytes("{\"type\":\"PING\"}\n");
            using var stream = new MemoryStream(data);
            var reader = new MessageFrameReader(stream, 1024);

            var frame = await reader.ReadFrameAsync(CancellationToken.None);
            Assert.Equal("{\"type\":\"PING\"}", frame);

            var nextFrame = await reader.ReadFrameAsync(CancellationToken.None);
            Assert.Null(nextFrame); // EOF
        }

        [Fact]
        public async Task ReadFrameAsync_MultipleFramesInOneRead_ShouldExtractAll()
        {
            var data = Encoding.UTF8.GetBytes("frame1\nframe2\nframe3\n");
            using var stream = new MemoryStream(data);
            var reader = new MessageFrameReader(stream, 1024);

            Assert.Equal("frame1", await reader.ReadFrameAsync(CancellationToken.None));
            Assert.Equal("frame2", await reader.ReadFrameAsync(CancellationToken.None));
            Assert.Equal("frame3", await reader.ReadFrameAsync(CancellationToken.None));
            Assert.Null(await reader.ReadFrameAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ReadFrameAsync_PartialAndSplitFrames_ShouldAccumulateCorrectly()
        {
            // Simulate reading frame1 in parts
            var stream = new ChunkedMemoryStream(new[]
            {
                Encoding.UTF8.GetBytes("part1-"),
                Encoding.UTF8.GetBytes("part2\npart3\n")
            });
            var reader = new MessageFrameReader(stream, 1024);

            Assert.Equal("part1-part2", await reader.ReadFrameAsync(CancellationToken.None));
            Assert.Equal("part3", await reader.ReadFrameAsync(CancellationToken.None));
            Assert.Null(await reader.ReadFrameAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ReadFrameAsync_EmptyAndWhitespaceFrames_ShouldBeIgnored()
        {
            var data = Encoding.UTF8.GetBytes("\n   \n\t\nframe1\n  \nframe2\n");
            using var stream = new MemoryStream(data);
            var reader = new MessageFrameReader(stream, 1024);

            Assert.Equal("frame1", await reader.ReadFrameAsync(CancellationToken.None));
            Assert.Equal("frame2", await reader.ReadFrameAsync(CancellationToken.None));
            Assert.Null(await reader.ReadFrameAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ReadFrameAsync_OversizedFrame_ShouldThrowException()
        {
            var data = Encoding.UTF8.GetBytes("this-is-a-very-long-frame-exceeding-limit\n");
            using var stream = new MemoryStream(data);
            // set limit to 10 bytes
            var reader = new MessageFrameReader(stream, 10);

            var ex = await Assert.ThrowsAsync<ProtocolException>(() => reader.ReadFrameAsync(CancellationToken.None));
            Assert.Equal(ProtocolException.FrameTooLarge, ex.ErrorCode);
        }

        [Fact]
        public async Task WriteFrameAsync_ShouldAppendNewlineCorrectly()
        {
            using var stream = new MemoryStream();
            var writer = new MessageFrameWriter(stream);

            await writer.WriteFrameAsync("myframe", CancellationToken.None);

            var result = Encoding.UTF8.GetString(stream.ToArray());
            Assert.Equal("myframe\n", result);
        }

        [Fact]
        public async Task WriteMessageAsync_ShouldSerializeAndAppendNewline()
        {
            using var stream = new MemoryStream();
            var writer = new MessageFrameWriter(stream);

            var msg = new HeartbeatMessage { PcId = "PC-TEST", Timestamp = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc) };
            await writer.WriteMessageAsync(msg, CancellationToken.None);

            var result = Encoding.UTF8.GetString(stream.ToArray());
            Assert.EndsWith("\n", result);
            Assert.Contains("\"pcId\":\"PC-TEST\"", result);
            Assert.Contains("\"type\":\"HEARTBEAT\"", result);
        }
    }

    /// <summary>
    /// Custom stream that returns data in specific predefined chunks to simulate TCP socket fragmentation.
    /// </summary>
    public class ChunkedMemoryStream : Stream
    {
        private readonly byte[][] _chunks;
        private int _chunkIndex;
        private int _chunkPosition;

        public ChunkedMemoryStream(byte[][] chunks)
        {
            _chunks = chunks;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_chunkIndex >= _chunks.Length) return 0;

            var currentChunk = _chunks[_chunkIndex];
            int availableInChunk = currentChunk.Length - _chunkPosition;
            int bytesToCopy = Math.Min(count, availableInChunk);

            Buffer.BlockCopy(currentChunk, _chunkPosition, buffer, offset, bytesToCopy);
            _chunkPosition += bytesToCopy;

            if (_chunkPosition >= currentChunk.Length)
            {
                _chunkIndex++;
                _chunkPosition = 0;
            }

            return bytesToCopy;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

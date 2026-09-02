using Sayra.Backend.Application.Configuration;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class ConfigurationHashTests
    {
        private readonly ICanonicalConfigurationSerializer _serializer = new CanonicalConfigurationSerializer();
        private readonly IConfigurationHashService _hashService;

        public ConfigurationHashTests()
        {
            _hashService = new ConfigurationHashService(_serializer);
        }

        [Fact]
        public void ComputeHash_SameLogicalConfiguration_ProducesIdenticalHash()
        {
            string payloadA = @"{""b"": 2, ""a"": 1}";
            string payloadB = @"{
                ""a"": 1,
                ""b"": 2
            }";

            string hashA = _hashService.ComputeHash(payloadA);
            string hashB = _hashService.ComputeHash(payloadB);

            Assert.Equal(hashA, hashB);
        }

        [Fact]
        public void ComputeHash_MeaningfulValueChange_ProducesDifferentHash()
        {
            string payloadA = @"{""a"": 1, ""b"": 2}";
            string payloadB = @"{""a"": 1, ""b"": 3}";

            string hashA = _hashService.ComputeHash(payloadA);
            string hashB = _hashService.ComputeHash(payloadB);

            Assert.NotEqual(hashA, hashB);
        }

        [Fact]
        public void VerifyHash_ValidAndInvalidHashes_HandledCorrectly()
        {
            string payload = @"{""server"": {""port"": 5000}}";
            string validHash = _hashService.ComputeHash(payload);

            Assert.True(_hashService.VerifyHash(payload, validHash));
            Assert.True(_hashService.VerifyHash(payload, validHash.ToUpperInvariant())); // Casing insensitive check
            Assert.False(_hashService.VerifyHash(payload, "0000000000000000000000000000000000000000000000000000000000000000"));
            Assert.False(_hashService.VerifyHash(@"{""server"": {""port"": 5001}}", validHash)); // Tampered payload
        }
    }
}

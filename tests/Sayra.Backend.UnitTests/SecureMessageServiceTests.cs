using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Infrastructure.Security;
using Sayra.Backend.Infrastructure.Transport;

namespace Sayra.Backend.UnitTests
{
    public class SecureMessageServiceTests
    {
        private readonly ICryptographicService _cryptoService;
        private readonly TcpConnectionRegistry _connectionRegistry;
        private readonly SecureMessageService _secureMessageService;
        private readonly byte[] _sessionKey;

        public SecureMessageServiceTests()
        {
            _cryptoService = new CryptographicService();
            _connectionRegistry = new TcpConnectionRegistry();
            _secureMessageService = new SecureMessageService(
                _cryptoService,
                _connectionRegistry,
                NullLogger<SecureMessageService>.Instance);

            _sessionKey = RandomNumberGenerator.GetBytes(32);
        }

        #region Encryption Tests

        [Fact]
        public void Encrypt_And_Decrypt_Success()
        {
            // Arrange
            var payload = new { Command = "PING", Data = "Hello" };

            // Act
            var envelope = _secureMessageService.EncryptAndSign(payload, _sessionKey);
            var result = _secureMessageService.DecryptAndVerify(envelope, _sessionKey);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.PlaintextPayload);
            Assert.Contains("PING", result.PlaintextPayload);
            Assert.Contains("Hello", result.PlaintextPayload);
        }

        [Fact]
        public void Decrypt_With_Invalid_Key_Rejection()
        {
            // Arrange
            var payload = new { Command = "PING" };
            var envelope = _secureMessageService.EncryptAndSign(payload, _sessionKey);
            var invalidKey = RandomNumberGenerator.GetBytes(32);

            // Act
            var result = _secureMessageService.DecryptAndVerify(envelope, invalidKey);

            // Assert
            // When key is invalid, either HMAC verification fails or AES decryption fails.
            // In either case, validation should fail.
            Assert.False(result.IsSuccess);
            Assert.True(result.ErrorCode == "SIGNATURE_MISMATCH" || result.ErrorCode == "DECRYPTION_FAILED");
        }

        [Fact]
        public void Decrypt_With_Corrupted_Payload_Rejection()
        {
            // Arrange
            var payload = new { Command = "PING" };
            var envelope = _secureMessageService.EncryptAndSign(payload, _sessionKey);

            // Corrupt the payload bytes (invalid base64, or different signature inputs)
            envelope.Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("CorruptedPayloadJunkThatIsNotValidAesFormatLength"));

            // Act
            var result = _secureMessageService.DecryptAndVerify(envelope, _sessionKey);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("SIGNATURE_MISMATCH", result.ErrorCode);
        }

        #endregion

        #region Signature Tests

        [Fact]
        public void Valid_Signature_Accepted()
        {
            // Arrange
            var payload = new { Message = "Secure" };
            var envelope = _secureMessageService.EncryptAndSign(payload, _sessionKey);

            // Act
            var result = _secureMessageService.DecryptAndVerify(envelope, _sessionKey);

            // Assert
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void Modified_Payload_Rejected()
        {
            // Arrange
            var payload = new { Message = "Secure" };
            var envelope = _secureMessageService.EncryptAndSign(payload, _sessionKey);

            // Modify payload Base64 content slightly to trigger signature failure
            byte[] rawBytes = Convert.FromBase64String(envelope.Payload);
            rawBytes[rawBytes.Length - 1] ^= 0x01; // flip last bit
            envelope.Payload = Convert.ToBase64String(rawBytes);

            // Act
            var result = _secureMessageService.DecryptAndVerify(envelope, _sessionKey);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("SIGNATURE_MISMATCH", result.ErrorCode);
        }

        [Fact]
        public void Modified_Timestamp_Rejected()
        {
            // Arrange
            var payload = new { Message = "Secure" };
            var envelope = _secureMessageService.EncryptAndSign(payload, _sessionKey);

            // Modify timestamp slightly
            envelope.Timestamp = DateTime.UtcNow.AddSeconds(10).ToString("yyyy-MM-ddTHH:mm:ssZ");

            // Act
            var result = _secureMessageService.DecryptAndVerify(envelope, _sessionKey);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("SIGNATURE_MISMATCH", result.ErrorCode);
        }

        #endregion

        #region Replay Protection Tests

        [Fact]
        public void Valid_Timestamp_Accepted()
        {
            // Arrange
            var payload = new { Message = "Secure" };
            var envelope = _secureMessageService.EncryptAndSign(payload, _sessionKey);

            // Act
            var result = _secureMessageService.DecryptAndVerify(envelope, _sessionKey);

            // Assert
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void Old_Timestamp_Rejected()
        {
            // Arrange
            var payload = new { Message = "Secure" };
            var envelope = _secureMessageService.EncryptAndSign(payload, _sessionKey);

            // Make timestamp 10 minutes (600 seconds) old
            string oldTimestamp = DateTime.UtcNow.AddMinutes(-10).ToString("yyyy-MM-ddTHH:mm:ssZ");

            // Re-generate signature for this old timestamp so only replay window check fails, not signature verification
            string signatureInput = envelope.Payload + "|" + oldTimestamp;
            byte[] signatureInputBytes = Encoding.UTF8.GetBytes(signatureInput);
            byte[] hmacBytes = _cryptoService.ComputeHmacSha256(signatureInputBytes, _sessionKey);

            envelope.Timestamp = oldTimestamp;
            envelope.Signature = Convert.ToBase64String(hmacBytes);

            // Act
            var result = _secureMessageService.DecryptAndVerify(envelope, _sessionKey);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("TIMESTAMP_DRIFT_EXCEEDED", result.ErrorCode);
        }

        [Fact]
        public void Future_Timestamp_Rejected()
        {
            // Arrange
            var payload = new { Message = "Secure" };
            var envelope = _secureMessageService.EncryptAndSign(payload, _sessionKey);

            // Make timestamp 10 minutes in the future
            string futureTimestamp = DateTime.UtcNow.AddMinutes(10).ToString("yyyy-MM-ddTHH:mm:ssZ");

            // Re-generate signature for this future timestamp so signature matches, but replay window fails
            string signatureInput = envelope.Payload + "|" + futureTimestamp;
            byte[] signatureInputBytes = Encoding.UTF8.GetBytes(signatureInput);
            byte[] hmacBytes = _cryptoService.ComputeHmacSha256(signatureInputBytes, _sessionKey);

            envelope.Timestamp = futureTimestamp;
            envelope.Signature = Convert.ToBase64String(hmacBytes);

            // Act
            var result = _secureMessageService.DecryptAndVerify(envelope, _sessionKey);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("TIMESTAMP_DRIFT_EXCEEDED", result.ErrorCode);
        }

        #endregion
    }
}

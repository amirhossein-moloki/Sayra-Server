using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Security;
using SecureMessageEnvelope = Sayra.Backend.Application.Security.SecureMessageEnvelope;
using SequenceValidator = Sayra.Backend.Application.Security.SequenceValidator;
using Sayra.Backend.Infrastructure.Security;
using Sayra.Backend.Infrastructure.Transport;

namespace Sayra.Backend.UnitTests
{
    public class SecureEnvelopeSecurityTests
    {
        private readonly ICryptographicService _cryptoService;
        private readonly TcpConnectionRegistry _connectionRegistry;
        private readonly SequenceValidator _sequenceValidator;
        private readonly SecureMessageService _secureMessageService;
        private readonly byte[] _sessionKey;

        public SecureEnvelopeSecurityTests()
        {
            _cryptoService = new CryptographicService();
            _connectionRegistry = new TcpConnectionRegistry();
            _sequenceValidator = new SequenceValidator();
            _secureMessageService = new SecureMessageService(
                _cryptoService,
                _connectionRegistry,
                NullLogger<SecureMessageService>.Instance,
                _sequenceValidator);

            _sessionKey = RandomNumberGenerator.GetBytes(32);
        }

        #region 1. Encryption & Decryption Tests

        [Fact]
        public void EncryptAndDecrypt_Success()
        {
            var payload = new { Action = "START", GamerId = Guid.NewGuid(), SessionId = Guid.NewGuid() };
            var envelope = _secureMessageService.EncryptAndSign(payload, _sessionKey);

            var result = _secureMessageService.DecryptAndVerify(envelope, _sessionKey);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.PlaintextPayload);
            Assert.Contains("START", result.PlaintextPayload);
        }

        [Fact]
        public void Decrypt_WithWrongSessionKey_Fails()
        {
            var payload = new { Data = "SensitiveContent" };
            var envelope = _secureMessageService.EncryptAndSign(payload, _sessionKey);
            var wrongKey = RandomNumberGenerator.GetBytes(32);

            var result = _secureMessageService.DecryptAndVerify(envelope, wrongKey);

            Assert.False(result.IsSuccess);
            Assert.True(result.ErrorCode == "SIGNATURE_MISMATCH" || result.ErrorCode == "DECRYPTION_FAILED");
        }

        [Fact]
        public void Decrypt_CorruptedCiphertext_Fails()
        {
            var payload = new { Data = "SensitiveContent" };
            var envelope = _secureMessageService.EncryptAndSign(payload, _sessionKey);

            // Corrupt payload bytes
            byte[] rawBytes = Convert.FromBase64String(envelope.Payload);
            rawBytes[rawBytes.Length - 1] ^= 0xFF;
            envelope.Payload = Convert.ToBase64String(rawBytes);

            var result = _secureMessageService.DecryptAndVerify(envelope, _sessionKey);

            Assert.False(result.IsSuccess);
            Assert.Equal("SIGNATURE_MISMATCH", result.ErrorCode);
        }

        [Fact]
        public void Decrypt_PayloadTooShort_Fails()
        {
            var shortBytes = new byte[8]; // Less than 16-byte IV
            var envelope = new SecureMessageEnvelope
            {
                Payload = Convert.ToBase64String(shortBytes),
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ProtocolVersion = "1.0"
            };

            // Sign envelope
            string signatureInput = envelope.Payload + "|" + envelope.Timestamp;
            byte[] signature = _cryptoService.ComputeHmacSha256(Encoding.UTF8.GetBytes(signatureInput), _sessionKey);
            envelope.Signature = Convert.ToBase64String(signature);

            var result = _secureMessageService.DecryptAndVerify(envelope, _sessionKey);

            Assert.False(result.IsSuccess);
            Assert.Equal("PAYLOAD_TOO_SHORT", result.ErrorCode);
        }

        #endregion

        #region 2. HMAC Integrity & Constant Time Tests

        [Fact]
        public void HMAC_ValidSignature_Accepted()
        {
            var payload = new { Test = "HMAC" };
            var envelope = _secureMessageService.EncryptAndSign(payload, _sessionKey);

            var result = _secureMessageService.DecryptAndVerify(envelope, _sessionKey);

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void HMAC_TamperedSignature_Rejected()
        {
            var payload = new { Test = "HMAC" };
            var envelope = _secureMessageService.EncryptAndSign(payload, _sessionKey);

            // Tamper signature
            byte[] sigBytes = Convert.FromBase64String(envelope.Signature);
            sigBytes[0] ^= 0x01;
            envelope.Signature = Convert.ToBase64String(sigBytes);

            var result = _secureMessageService.DecryptAndVerify(envelope, _sessionKey);

            Assert.False(result.IsSuccess);
            Assert.Equal("SIGNATURE_MISMATCH", result.ErrorCode);
        }

        [Fact]
        public void HMAC_InvalidBase64Signature_Rejected()
        {
            var envelope = new SecureMessageEnvelope
            {
                Payload = Convert.ToBase64String(new byte[32]),
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Signature = "NotValidBase64!!!"
            };

            var result = _secureMessageService.DecryptAndVerify(envelope, _sessionKey);

            Assert.False(result.IsSuccess);
            Assert.Equal("INVALID_SIGNATURE_BASE64", result.ErrorCode);
        }

        #endregion

        #region 3. Replay Protection & Sequence Number Tests

        [Fact]
        public void SequenceValidator_MonotonicSequence_Success()
        {
            string sessionId = "SESSION-CONN-01";

            long s1 = _sequenceValidator.GetNextOutboundSequence(sessionId);
            long s2 = _sequenceValidator.GetNextOutboundSequence(sessionId);
            long s3 = _sequenceValidator.GetNextOutboundSequence(sessionId);

            Assert.Equal(1, s1);
            Assert.Equal(2, s2);
            Assert.Equal(3, s3);
        }

        [Fact]
        public void SequenceValidator_InboundSequenceProgression_Success()
        {
            string sessionId = "SESSION-CONN-02";

            Assert.True(_sequenceValidator.ValidateInboundSequence(sessionId, 100, "MSG-01"));
            Assert.True(_sequenceValidator.ValidateInboundSequence(sessionId, 101, "MSG-02"));
            Assert.True(_sequenceValidator.ValidateInboundSequence(sessionId, 102, "MSG-03"));
        }

        [Fact]
        public void SequenceValidator_ReplayedSequence_Fails()
        {
            string sessionId = "SESSION-CONN-03";

            Assert.True(_sequenceValidator.ValidateInboundSequence(sessionId, 50, "MSG-50"));
            Assert.False(_sequenceValidator.ValidateInboundSequence(sessionId, 50, "MSG-50-DUP")); // Same sequence
            Assert.False(_sequenceValidator.ValidateInboundSequence(sessionId, 49, "MSG-49"));     // Lower sequence
        }

        [Fact]
        public void SequenceValidator_ReplayedMessageId_Fails()
        {
            string sessionId = "SESSION-CONN-04";

            Assert.True(_sequenceValidator.ValidateInboundSequence(sessionId, 0, "MSG-UNIQUE-01"));
            Assert.False(_sequenceValidator.ValidateInboundSequence(sessionId, 0, "MSG-UNIQUE-01")); // Duplicate MessageId
        }

        [Fact]
        public void HandleSecureMessage_ReplayedEnvelope_Rejected()
        {
            var session = new ConnectionSession
            {
                ConnectionId = "CONN-REPLAY-TEST",
                PcId = "PC-REPLAY",
                SessionKey = _sessionKey,
                HandshakeState = Sayra.Backend.Domain.ConnectionLifecycleState.Active
            };

            var payload = new { Command = "HEARTBEAT" };
            var envelope = _secureMessageService.EncryptAndSign(
                payload,
                _sessionKey,
                sessionId: session.ConnectionId,
                sequenceNumber: 10,
                messageId: "MSG-REPLAY-1001");

            // First handle should succeed
            var r1 = _secureMessageService.HandleSecureMessageAsync(session, envelope).GetAwaiter().GetResult();
            Assert.True(r1.IsSuccess);

            // Second handle with exact same envelope should fail with REPLAY_DETECTED
            var r2 = _secureMessageService.HandleSecureMessageAsync(session, envelope).GetAwaiter().GetResult();
            Assert.False(r2.IsSuccess);
            Assert.Equal("REPLAY_DETECTED", r2.ErrorCode);
        }

        #endregion

        #region 4. Session Binding Tests

        [Fact]
        public void HandleSecureMessage_MatchingSessionId_Accepted()
        {
            var session = new ConnectionSession
            {
                ConnectionId = "CONN-BOUND-01",
                PcId = "PC-BOUND-01",
                SessionKey = _sessionKey,
                HandshakeState = Sayra.Backend.Domain.ConnectionLifecycleState.Active
            };

            var payload = new { Action = "PING" };
            var envelope = _secureMessageService.EncryptAndSign(
                payload,
                _sessionKey,
                sessionId: session.ConnectionId);

            var result = _secureMessageService.HandleSecureMessageAsync(session, envelope).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void HandleSecureMessage_MismatchedSessionId_Rejected()
        {
            var session = new ConnectionSession
            {
                ConnectionId = "CONN-BOUND-REAL",
                PcId = "PC-REAL",
                SessionKey = _sessionKey,
                HandshakeState = Sayra.Backend.Domain.ConnectionLifecycleState.Active
            };

            var payload = new { Action = "PING" };
            var envelope = _secureMessageService.EncryptAndSign(
                payload,
                _sessionKey,
                sessionId: "CONN-BOUND-ATTACKER"); // Mismatched session ID

            var result = _secureMessageService.HandleSecureMessageAsync(session, envelope).GetAwaiter().GetResult();

            Assert.False(result.IsSuccess);
            Assert.Equal("SESSION_BINDING_FAILED", result.ErrorCode);
        }

        #endregion

        #region 5. Protocol Version & Limit Tests

        [Fact]
        public void Decrypt_UnsupportedProtocolVersion_Fails()
        {
            var payload = new { Action = "PING" };
            var envelope = _secureMessageService.EncryptAndSign(payload, _sessionKey);
            envelope.ProtocolVersion = "2.0"; // Unsupported

            var result = _secureMessageService.DecryptAndVerify(envelope, _sessionKey);

            Assert.False(result.IsSuccess);
            Assert.Equal("PROTOCOL_VERSION_REJECTED", result.ErrorCode);
        }

        [Fact]
        public void Decrypt_StaleTimestampDrift_Fails()
        {
            var payload = new { Action = "PING" };
            var envelope = _secureMessageService.EncryptAndSign(payload, _sessionKey);
            envelope.Timestamp = DateTime.UtcNow.AddMinutes(-10).ToString("yyyy-MM-ddTHH:mm:ssZ");

            // Re-sign with stale timestamp
            string signatureInput = envelope.Payload + "|" + envelope.Timestamp;
            byte[] signature = _cryptoService.ComputeHmacSha256(Encoding.UTF8.GetBytes(signatureInput), _sessionKey);
            envelope.Signature = Convert.ToBase64String(signature);

            var result = _secureMessageService.DecryptAndVerify(envelope, _sessionKey);

            Assert.False(result.IsSuccess);
            Assert.Equal("TIMESTAMP_DRIFT_EXCEEDED", result.ErrorCode);
        }

        #endregion

        #region 6. Concurrency & Thread Safety Tests

        [Fact]
        public void Concurrency_MonotonicSequence_MultiThreaded()
        {
            string sessionId = "CONN-CONCURRENT-01";
            int threadCount = 10;
            int iterationsPerThread = 1000;
            var sequenceNumbers = new System.Collections.Concurrent.ConcurrentBag<long>();

            Parallel.For(0, threadCount, _ =>
            {
                for (int i = 0; i < iterationsPerThread; i++)
                {
                    sequenceNumbers.Add(_sequenceValidator.GetNextOutboundSequence(sessionId));
                }
            });

            Assert.Equal(threadCount * iterationsPerThread, sequenceNumbers.Count);

            var uniqueNumbers = new HashSet<long>(sequenceNumbers);
            Assert.Equal(threadCount * iterationsPerThread, uniqueNumbers.Count);
        }

        #endregion

        #region 7. Fuzzing & Robustness Tests

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("NON_EXISTENT_BASE64_###")]
        [InlineData("{ 'not': 'an envelope' }")]
        public void Fuzz_MalformedInputs_FailGracefullyWithoutCrash(string malformedInput)
        {
            var envelope = new SecureMessageEnvelope
            {
                Payload = malformedInput,
                Signature = malformedInput,
                Timestamp = malformedInput
            };

            var result = _secureMessageService.DecryptAndVerify(envelope, _sessionKey);

            Assert.False(result.IsSuccess);
            Assert.NotNull(result.ErrorCode);
        }

        [Fact]
        public void Fuzz_RandomBytesAsEnvelope_FailGracefully()
        {
            byte[] randomBytes = new byte[256];
            RandomNumberGenerator.Fill(randomBytes);

            var envelope = new SecureMessageEnvelope
            {
                Payload = Convert.ToBase64String(randomBytes),
                Signature = Convert.ToBase64String(randomBytes),
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };

            var result = _secureMessageService.DecryptAndVerify(envelope, _sessionKey);

            Assert.False(result.IsSuccess);
            Assert.NotNull(result.ErrorCode);
        }

        #endregion
    }
}

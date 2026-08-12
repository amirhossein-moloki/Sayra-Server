using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Application.Security
{
    public class SecureMessageService : ISecureMessageService
    {
        private readonly ICryptographicService _cryptographicService;
        private readonly ITcpConnectionRegistry _connectionRegistry;
        private readonly ILogger<SecureMessageService> _logger;

        public SecureMessageService(
            ICryptographicService cryptographicService,
            ITcpConnectionRegistry connectionRegistry,
            ILogger<SecureMessageService> logger)
        {
            _cryptographicService = cryptographicService ?? throw new ArgumentNullException(nameof(cryptographicService));
            _connectionRegistry = connectionRegistry ?? throw new ArgumentNullException(nameof(connectionRegistry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public SecureMessageEnvelope EncryptAndSign(object payload, byte[] sessionKey)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (sessionKey == null || sessionKey.Length != 32) throw new ArgumentException("SessionKey must be 32 bytes.", nameof(sessionKey));

            // 1. Serialize payload.
            string json = ProtocolSerialization.Serialize(payload);
            byte[] plainTextBytes = Encoding.UTF8.GetBytes(json);

            // 2. Generate IV & Encrypt using AES-256-CBC.
            byte[] iv = RandomNumberGenerator.GetBytes(16);
            byte[] encryptedBytes = _cryptographicService.EncryptAes256Cbc(plainTextBytes, sessionKey, iv);

            // Prepended IV format: [16 Bytes IV][Encrypted Payload]
            byte[] finalBytes = new byte[16 + encryptedBytes.Length];
            Buffer.BlockCopy(iv, 0, finalBytes, 0, 16);
            Buffer.BlockCopy(encryptedBytes, 0, finalBytes, 16, encryptedBytes.Length);
            string payloadBase64 = Convert.ToBase64String(finalBytes);

            // 3. Generate Timestamp
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            // 4. Generate Signature using HMAC-SHA256
            string signatureInput = payloadBase64 + "|" + timestamp;
            byte[] signatureInputBytes = Encoding.UTF8.GetBytes(signatureInput);
            byte[] hmacBytes = _cryptographicService.ComputeHmacSha256(signatureInputBytes, sessionKey);
            string signatureBase64 = Convert.ToBase64String(hmacBytes);

            return new SecureMessageEnvelope
            {
                Payload = payloadBase64,
                Signature = signatureBase64,
                Timestamp = timestamp
            };
        }

        public SecureMessageValidationResult DecryptAndVerify(SecureMessageEnvelope envelope, byte[] sessionKey)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (sessionKey == null || sessionKey.Length != 32) throw new ArgumentException("SessionKey must be 32 bytes.", nameof(sessionKey));

            // 1. Validate Timestamp
            if (string.IsNullOrWhiteSpace(envelope.Timestamp))
            {
                return Fail("MISSING_TIMESTAMP", "Timestamp is missing.");
            }

            if (!DateTime.TryParse(envelope.Timestamp, out var timestamp))
            {
                return Fail("INVALID_TIMESTAMP_FORMAT", "Timestamp format is invalid.");
            }

            var now = DateTime.UtcNow;
            var drift = Math.Abs((now - timestamp.ToUniversalTime()).TotalSeconds);
            if (drift > 300)
            {
                return Fail("TIMESTAMP_DRIFT_EXCEEDED", $"Timestamp drift of {drift}s exceeded 300s window.");
            }

            // 2. Validate HMAC Signature
            if (string.IsNullOrWhiteSpace(envelope.Payload))
            {
                return Fail("MISSING_PAYLOAD", "Payload is missing.");
            }

            if (string.IsNullOrWhiteSpace(envelope.Signature))
            {
                return Fail("MISSING_SIGNATURE", "Signature is missing.");
            }

            string signatureInput = envelope.Payload + "|" + envelope.Timestamp;
            byte[] signatureInputBytes = Encoding.UTF8.GetBytes(signatureInput);
            byte[] expectedHmac = _cryptographicService.ComputeHmacSha256(signatureInputBytes, sessionKey);

            byte[] clientSignatureBytes;
            try
            {
                clientSignatureBytes = Convert.FromBase64String(envelope.Signature);
            }
            catch (FormatException)
            {
                return Fail("INVALID_SIGNATURE_BASE64", "Signature Base64 is invalid.");
            }

            // Constant-time HMAC comparison
            if (!CryptographicOperations.FixedTimeEquals(expectedHmac, clientSignatureBytes))
            {
                return Fail("SIGNATURE_MISMATCH", "HMAC signature verification failed.");
            }

            // 3. Decrypt AES Payload
            byte[] rawPayloadBytes;
            try
            {
                rawPayloadBytes = Convert.FromBase64String(envelope.Payload);
            }
            catch (FormatException)
            {
                return Fail("INVALID_PAYLOAD_BASE64", "Payload Base64 is invalid.");
            }

            if (rawPayloadBytes.Length < 16)
            {
                return Fail("PAYLOAD_TOO_SHORT", "Payload is shorter than 16-byte IV.");
            }

            byte[] iv = new byte[16];
            byte[] ciphertext = new byte[rawPayloadBytes.Length - 16];
            Buffer.BlockCopy(rawPayloadBytes, 0, iv, 0, 16);
            Buffer.BlockCopy(rawPayloadBytes, 16, ciphertext, 0, ciphertext.Length);

            byte[] decryptedBytes;
            try
            {
                decryptedBytes = _cryptographicService.DecryptAes256Cbc(ciphertext, sessionKey, iv);
            }
            catch (Exception ex)
            {
                return Fail("DECRYPTION_FAILED", $"Payload decryption failed: {ex.Message}");
            }

            string decryptedPayload = Encoding.UTF8.GetString(decryptedBytes);

            return new SecureMessageValidationResult
            {
                IsSuccess = true,
                PlaintextPayload = decryptedPayload
            };
        }

        public async Task SendSecureMessageAsync(ConnectionSession session, object payload)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (payload == null) throw new ArgumentNullException(nameof(payload));

            if (session.SessionKey == null)
            {
                throw new InvalidOperationException("Session has no negotiated SessionKey.");
            }

            // Find the connection from the registry
            var connection = _connectionRegistry.Get(session.ConnectionId);
            if (connection == null)
            {
                throw new InvalidOperationException($"Connection not found for session ID: {session.ConnectionId}");
            }

            // Encrypt and sign payload
            var envelope = EncryptAndSign(payload, session.SessionKey);

            // Send through active TLS socket
            var stream = connection.GetStream();
            string envelopeJson = ProtocolSerialization.Serialize(envelope) + "\n";
            byte[] bytesToSend = Encoding.UTF8.GetBytes(envelopeJson);

            await stream.WriteAsync(bytesToSend, 0, bytesToSend.Length);
            await stream.FlushAsync();

            _logger.LogInformation(
                "Successfully sent secure message. ConnectionId: {ConnectionId}, PcId: {PcId}, MessageType: {MessageType}, Timestamp: {Timestamp}",
                session.ConnectionId,
                session.PcId,
                payload.GetType().Name,
                envelope.Timestamp);
        }

        public async Task<SecureMessageValidationResult> HandleSecureMessageAsync(ConnectionSession session, SecureMessageEnvelope envelope)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));

            if (session.SessionKey == null)
            {
                _logger.LogWarning(
                    "Security Event: Rejected unauthenticated secure message. ConnectionId: {ConnectionId}, PcId: {PcId}, ValidationResult: Failed",
                    session.ConnectionId,
                    session.PcId);

                return new SecureMessageValidationResult
                {
                    IsSuccess = false,
                    ErrorCode = "UNAUTHENTICATED_SESSION",
                    ErrorMessage = "Session has no negotiated SessionKey."
                };
            }

            var result = DecryptAndVerify(envelope, session.SessionKey);

            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Security Event: Secure message validation failed. ConnectionId: {ConnectionId}, PcId: {PcId}, ValidationResult: Failed, ErrorCode: {ErrorCode}, ErrorMessage: {ErrorMessage}",
                    session.ConnectionId,
                    session.PcId,
                    result.ErrorCode,
                    result.ErrorMessage);
            }
            else
            {
                _logger.LogInformation(
                    "Secure message processed successfully. ConnectionId: {ConnectionId}, PcId: {PcId}, ValidationResult: Success, Timestamp: {Timestamp}",
                    session.ConnectionId,
                    session.PcId,
                    envelope.Timestamp);
            }

            return await Task.FromResult(result);
        }

        private static SecureMessageValidationResult Fail(string errorCode, string message)
        {
            return new SecureMessageValidationResult
            {
                IsSuccess = false,
                ErrorCode = errorCode,
                ErrorMessage = message
            };
        }
    }
}

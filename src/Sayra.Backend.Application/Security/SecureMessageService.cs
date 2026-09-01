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
        private readonly ISequenceValidator? _sequenceValidator;
        private readonly ISecurityEventService? _securityEventService;
        private readonly ILogger<SecureMessageService> _logger;

        private const int MaxPayloadLengthBytes = 10 * 1024 * 1024; // 10MB limit

        public SecureMessageService(
            ICryptographicService cryptographicService,
            ITcpConnectionRegistry connectionRegistry,
            ILogger<SecureMessageService> logger,
            ISequenceValidator? sequenceValidator = null,
            ISecurityEventService? securityEventService = null)
        {
            _cryptographicService = cryptographicService ?? throw new ArgumentNullException(nameof(cryptographicService));
            _connectionRegistry = connectionRegistry ?? throw new ArgumentNullException(nameof(connectionRegistry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sequenceValidator = sequenceValidator;
            _securityEventService = securityEventService;
        }

        public SecureMessageEnvelope EncryptAndSign(object payload, byte[] sessionKey)
        {
            return EncryptAndSign(payload, sessionKey, sessionId: null, sequenceNumber: null);
        }

        public SecureMessageEnvelope EncryptAndSign(
            object payload,
            byte[] sessionKey,
            string? sessionId = null,
            long? sequenceNumber = null,
            string? messageId = null,
            string? messageType = null,
            string? correlationId = null,
            string? protocolVersion = "1.0")
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

            var envelope = new SecureMessageEnvelope
            {
                Payload = payloadBase64,
                Timestamp = timestamp,
                MessageId = messageId ?? Guid.NewGuid().ToString("N"),
                CorrelationId = correlationId,
                SessionId = sessionId,
                SequenceNumber = sequenceNumber,
                MessageType = messageType ?? payload.GetType().Name,
                ProtocolVersion = protocolVersion ?? "1.0"
            };

            // 4. Generate Signature using HMAC-SHA256 over full metadata signature input
            string signatureInput = ConstructExtendedSignatureInput(envelope);
            byte[] signatureInputBytes = Encoding.UTF8.GetBytes(signatureInput);
            byte[] hmacBytes = _cryptographicService.ComputeHmacSha256(signatureInputBytes, sessionKey);
            envelope.Signature = Convert.ToBase64String(hmacBytes);

            return envelope;
        }

        public SecureMessageValidationResult DecryptAndVerify(SecureMessageEnvelope envelope, byte[] sessionKey)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (sessionKey == null || sessionKey.Length != 32) throw new ArgumentException("SessionKey must be 32 bytes.", nameof(sessionKey));

            // 1. Validate Protocol Version
            if (!string.IsNullOrWhiteSpace(envelope.ProtocolVersion) && envelope.ProtocolVersion != "1.0")
            {
                return Fail("PROTOCOL_VERSION_REJECTED", $"Unsupported protocol version: '{envelope.ProtocolVersion}'.");
            }

            // 2. Validate Timestamp
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

            // 3. Validate HMAC Signature
            if (string.IsNullOrWhiteSpace(envelope.Payload))
            {
                return Fail("MISSING_PAYLOAD", "Payload is missing.");
            }

            if (string.IsNullOrWhiteSpace(envelope.Signature))
            {
                return Fail("MISSING_SIGNATURE", "Signature is missing.");
            }

            byte[] clientSignatureBytes;
            try
            {
                clientSignatureBytes = Convert.FromBase64String(envelope.Signature);
            }
            catch (FormatException)
            {
                return Fail("INVALID_SIGNATURE_BASE64", "Signature Base64 is invalid.");
            }

            // Extended signature input covers all metadata fields
            bool isSignatureValid = false;
            string extendedInput = ConstructExtendedSignatureInput(envelope);
            byte[] expectedHmacExtended = _cryptographicService.ComputeHmacSha256(Encoding.UTF8.GetBytes(extendedInput), sessionKey);
            if (CryptographicOperations.FixedTimeEquals(expectedHmacExtended, clientSignatureBytes))
            {
                isSignatureValid = true;
            }
            else
            {
                // Fallback check for legacy/basic envelopes (where metadata fields like SessionId/SequenceNumber were not set)
                string basicInput = envelope.Payload + "|" + envelope.Timestamp;
                byte[] expectedHmacBasic = _cryptographicService.ComputeHmacSha256(Encoding.UTF8.GetBytes(basicInput), sessionKey);
                if (CryptographicOperations.FixedTimeEquals(expectedHmacBasic, clientSignatureBytes))
                {
                    isSignatureValid = true;
                }
            }

            if (!isSignatureValid)
            {
                return Fail("SIGNATURE_MISMATCH", "HMAC signature verification failed.");
            }

            // 4. Decrypt AES Payload
            byte[] rawPayloadBytes;
            try
            {
                rawPayloadBytes = Convert.FromBase64String(envelope.Payload);
            }
            catch (FormatException)
            {
                return Fail("INVALID_PAYLOAD_BASE64", "Payload Base64 is invalid.");
            }

            if (rawPayloadBytes.Length > MaxPayloadLengthBytes)
            {
                return Fail("PAYLOAD_LIMIT_EXCEEDED", $"Payload length ({rawPayloadBytes.Length} bytes) exceeds maximum allowed limit.");
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

            var connection = _connectionRegistry.Get(session.ConnectionId);
            if (connection == null)
            {
                throw new InvalidOperationException($"Connection not found for session ID: {session.ConnectionId}");
            }

            long? sequenceNumber = null;
            if (_sequenceValidator != null && !string.IsNullOrEmpty(session.ConnectionId))
            {
                sequenceNumber = _sequenceValidator.GetNextOutboundSequence(session.ConnectionId);
            }

            // Assign metadata BEFORE encrypting and signing
            var envelope = EncryptAndSign(
                payload,
                session.SessionKey,
                sessionId: session.ConnectionId,
                sequenceNumber: sequenceNumber,
                messageType: payload.GetType().Name);

            string envelopeJson = ProtocolSerialization.Serialize(envelope);
            await connection.SendFrameAsync(envelopeJson);

            _logger.LogInformation(
                "Successfully sent secure message. ConnectionId: {ConnectionId}, PcId: {PcId}, MessageType: {MessageType}, SequenceNumber: {Seq}, Timestamp: {Timestamp}",
                session.ConnectionId,
                session.PcId,
                payload.GetType().Name,
                envelope.SequenceNumber,
                envelope.Timestamp);
        }

        public async Task<SecureMessageValidationResult> HandleSecureMessageAsync(ConnectionSession session, SecureMessageEnvelope envelope)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));

            if (session.SessionKey == null)
            {
                _logger.LogWarning(
                    "Security Event: Rejected unauthenticated secure message. ConnectionId: {ConnectionId}, PcId: {PcId}",
                    session.ConnectionId,
                    session.PcId);

                await RecordSecurityEventAsync(session, "UNAUTHENTICATED_SESSION", "Session has no negotiated SessionKey.");

                return new SecureMessageValidationResult
                {
                    IsSuccess = false,
                    ErrorCode = "UNAUTHENTICATED_SESSION",
                    ErrorMessage = "Session has no negotiated SessionKey."
                };
            }

            // 1. Session Binding Check
            if (!string.IsNullOrWhiteSpace(envelope.SessionId))
            {
                if (envelope.SessionId != session.ConnectionId && envelope.SessionId != session.PcId)
                {
                    _logger.LogWarning(
                        "Security Event: Session binding mismatch. Envelope SessionId: {EnvelopeSessionId}, ConnectionId: {ConnectionId}, PcId: {PcId}",
                        envelope.SessionId,
                        session.ConnectionId,
                        session.PcId);

                    var failResult = Fail("SESSION_BINDING_FAILED", $"Session binding mismatch. Envelope SessionId '{envelope.SessionId}' does not match session.");
                    await RecordSecurityEventAsync(session, "SESSION_BINDING_FAILED", failResult.ErrorMessage);
                    return failResult;
                }
            }

            // 2. Replay Protection & Sequence Validation Check
            if (_sequenceValidator != null && !string.IsNullOrEmpty(session.ConnectionId))
            {
                long seq = envelope.SequenceNumber ?? 0;
                if (seq > 0 || !string.IsNullOrEmpty(envelope.MessageId))
                {
                    bool isValidSeq = _sequenceValidator.ValidateInboundSequence(session.ConnectionId, seq, envelope.MessageId);
                    if (!isValidSeq)
                    {
                        _logger.LogWarning(
                            "Security Event: Replay detected on connection {ConnectionId}. SequenceNumber: {SequenceNumber}, MessageId: {MessageId}",
                            session.ConnectionId,
                            seq,
                            envelope.MessageId);

                        var replayResult = Fail("REPLAY_DETECTED", "Replayed or out-of-order sequence number or duplicate MessageId.");
                        await RecordSecurityEventAsync(session, "REPLAY_DETECTED", replayResult.ErrorMessage);
                        return replayResult;
                    }
                }
            }

            // 3. Cryptographic Verification & Decryption
            var result = DecryptAndVerify(envelope, session.SessionKey);

            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Security Event: Secure message validation failed. ConnectionId: {ConnectionId}, PcId: {PcId}, ErrorCode: {ErrorCode}, ErrorMessage: {ErrorMessage}",
                    session.ConnectionId,
                    session.PcId,
                    result.ErrorCode,
                    result.ErrorMessage);

                await RecordSecurityEventAsync(session, result.ErrorCode ?? "VERIFICATION_FAILED", result.ErrorMessage);
            }
            else
            {
                _logger.LogInformation(
                    "Secure message processed successfully. ConnectionId: {ConnectionId}, PcId: {PcId}, Timestamp: {Timestamp}",
                    session.ConnectionId,
                    session.PcId,
                    envelope.Timestamp);
            }

            return result;
        }

        private async Task RecordSecurityEventAsync(ConnectionSession session, string eventType, string? failureReason)
        {
            if (_securityEventService == null) return;

            try
            {
                Guid? resourceGuid = null;
                if (!string.IsNullOrEmpty(session.ConnectionId) && Guid.TryParse(session.ConnectionId, out var parsedGuid))
                {
                    resourceGuid = parsedGuid;
                }

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: $"SECURE_MESSAGE_{eventType}",
                    actorId: null,
                    actorType: "DEVICE",
                    deviceId: session.PcId ?? session.ConnectionId ?? "UNKNOWN",
                    organizationId: null,
                    siteId: null,
                    resourceType: "CommunicationSession",
                    resourceId: resourceGuid,
                    action: "ENVELOPE_PROCESSING",
                    result: "FAILED",
                    failureReason: failureReason,
                    cancellationToken: default);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record security audit event for envelope failure.");
            }
        }

        private static string ConstructExtendedSignatureInput(SecureMessageEnvelope envelope)
        {
            return $"{envelope.Payload}|{envelope.Timestamp}|{envelope.MessageId ?? ""}|{envelope.CorrelationId ?? ""}|{envelope.SessionId ?? ""}|{(envelope.SequenceNumber.HasValue ? envelope.SequenceNumber.Value.ToString() : "")}|{envelope.MessageType ?? ""}|{envelope.ProtocolVersion ?? ""}";
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

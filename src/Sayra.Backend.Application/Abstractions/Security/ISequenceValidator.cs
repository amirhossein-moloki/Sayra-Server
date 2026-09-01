using System;

#nullable enable

namespace Sayra.Backend.Application.Abstractions.Security
{
    public interface ISequenceValidator
    {
        /// <summary>
        /// Generates the next monotonic outbound sequence number for the session.
        /// </summary>
        long GetNextOutboundSequence(string sessionId);

        /// <summary>
        /// Validates whether an inbound sequence number and message ID are valid and not replayed.
        /// </summary>
        bool ValidateInboundSequence(string sessionId, long sequenceNumber, string? messageId = null);

        /// <summary>
        /// Resets and releases sequence tracking state for a terminated or disconnected session.
        /// </summary>
        void ResetSession(string sessionId);
    }
}

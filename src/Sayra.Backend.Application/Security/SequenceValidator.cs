using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Sayra.Backend.Application.Abstractions.Security;

#nullable enable

namespace Sayra.Backend.Application.Security
{
    public class SequenceValidator : ISequenceValidator
    {
        private class SessionSequenceState
        {
            public long OutboundSequence;
            public long LastInboundSequence;
            public readonly HashSet<string> SeenMessageIds = new(StringComparer.Ordinal);
            public readonly Queue<string> MessageIdOrder = new();
            public readonly object Lock = new();
        }

        private readonly ConcurrentDictionary<string, SessionSequenceState> _sessions = new(StringComparer.Ordinal);
        private const int MaxTrackedMessageIdsPerSession = 1000;

        public long GetNextOutboundSequence(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentNullException(nameof(sessionId));

            var state = _sessions.GetOrAdd(sessionId, _ => new SessionSequenceState());
            return Interlocked.Increment(ref state.OutboundSequence);
        }

        public bool ValidateInboundSequence(string sessionId, long sequenceNumber, string? messageId = null)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return false;

            var state = _sessions.GetOrAdd(sessionId, _ => new SessionSequenceState());

            lock (state.Lock)
            {
                // Sequence number check: if a non-zero sequence number is provided, it must be strictly increasing.
                if (sequenceNumber > 0)
                {
                    if (sequenceNumber <= state.LastInboundSequence)
                    {
                        return false; // Replayed or out-of-order sequence number
                    }
                    state.LastInboundSequence = sequenceNumber;
                }

                // Message ID deduplication check
                if (!string.IsNullOrEmpty(messageId))
                {
                    if (state.SeenMessageIds.Contains(messageId))
                    {
                        return false; // Replayed message ID
                    }

                    state.SeenMessageIds.Add(messageId);
                    state.MessageIdOrder.Enqueue(messageId);

                    // Bound tracking memory
                    if (state.MessageIdOrder.Count > MaxTrackedMessageIdsPerSession)
                    {
                        var oldestId = state.MessageIdOrder.Dequeue();
                        state.SeenMessageIds.Remove(oldestId);
                    }
                }

                return true;
            }
        }

        public void ResetSession(string sessionId)
        {
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                _sessions.TryRemove(sessionId, out _);
            }
        }
    }
}

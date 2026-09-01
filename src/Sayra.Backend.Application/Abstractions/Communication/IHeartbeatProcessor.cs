using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain.ValueObjects;

namespace Sayra.Backend.Application.Abstractions.Communication
{
    public interface IHeartbeatProcessor
    {
        Task<HeartbeatState> ProcessHeartbeatAsync(string connectionId, DateTime? timestamp = null, CancellationToken cancellationToken = default);
        Task EvaluateSessionLivenessAsync(TimeSpan timeoutThreshold, TimeSpan? degradedThreshold = null, CancellationToken cancellationToken = default);
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sayra.Backend.Application.Abstractions.Transport
{
    public interface ITcpSessionManager
    {
        Task RegisterSessionAsync(ITcpConnection connection, CancellationToken cancellationToken = default);
        Task TransitionStateAsync(string connectionId, ConnectionLifecycleState newState, CancellationToken cancellationToken = default);
        Task TransitionStateAsync(ITcpConnection connection, ConnectionLifecycleState newState, CancellationToken cancellationToken = default);
        Task HandleDisconnectAsync(string connectionId, string reason = "Normal Closure", CancellationToken cancellationToken = default);
        Task UpdateLastActivityAsync(string connectionId, CancellationToken cancellationToken = default);
        TcpConnectionContext? GetSessionContext(string connectionId);
        TcpConnectionContext? GetSessionContextByPcId(string pcId);
        IReadOnlyCollection<TcpConnectionContext> GetAllActiveSessions();
    }
}

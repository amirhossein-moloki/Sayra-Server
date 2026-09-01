using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Contracts;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Abstractions.Communication
{
    public interface IRemoteCommandManager
    {
        Task<Result<RemoteCommandResponseDto>> CreateAndDispatchCommandAsync(CreateRemoteCommandRequestDto request, CancellationToken cancellationToken = default);
        Task<Result<bool>> ProcessCommandAckAsync(string commandId, string pcId, string status, string? failureReason, CancellationToken cancellationToken = default);
        Task<Result<bool>> ProcessCommandResultAsync(string commandId, string pcId, string status, string? message, string? errorCode, string? resultPayload, CancellationToken cancellationToken = default);
        Task<Result<bool>> CancelCommandAsync(string commandId, string requestedBy, string? reason, CancellationToken cancellationToken = default);
        Task EvaluateTimeoutsAsync(CancellationToken cancellationToken = default);
    }
}

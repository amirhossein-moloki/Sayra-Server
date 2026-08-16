using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Sessions
{
    public interface ISessionStateTransitionService
    {
        Task<Result<bool>> ValidateNewSessionAsync(
            Guid gamerId,
            Guid workstationId,
            Guid? reservationId,
            CancellationToken cancellationToken = default);
    }
}

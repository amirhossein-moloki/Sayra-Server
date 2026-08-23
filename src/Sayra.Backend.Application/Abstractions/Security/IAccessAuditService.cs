using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sayra.Backend.Application.Abstractions.Security
{
    public interface IAccessAuditService
    {
        Task RecordAuthorizationGrantedAsync(
            UserPrincipal principal,
            string permission,
            object? resource = null,
            CancellationToken cancellationToken = default);

        Task RecordAuthorizationDeniedAsync(
            UserPrincipal principal,
            string permission,
            string reason,
            object? resource = null,
            CancellationToken cancellationToken = default);

        Task RecordDeviceHandshakeFailedAsync(
            string deviceId,
            string failureReason,
            string? ipAddress = null,
            CancellationToken cancellationToken = default);

        Task RecordDeviceRegisteredAsync(
            string deviceId,
            Guid? siteId = null,
            CancellationToken cancellationToken = default);
    }
}

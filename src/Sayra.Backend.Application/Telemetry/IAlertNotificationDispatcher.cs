using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.Telemetry
{
    public interface IAlertNotificationDispatcher
    {
        Task DispatchNotificationAsync(Incident incident, string notificationType, CancellationToken cancellationToken = default);
    }
}

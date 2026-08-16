using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Contracts;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Reservations
{
    public interface IReservationValidationService
    {
        Task<ReservationValidationResultDto> ValidateReservationAsync(
            Guid? reservationId,
            Guid? gamerId,
            Guid? siteId,
            Guid? workstationId,
            DateTime? checkTimeUtc,
            CancellationToken cancellationToken = default);

        Task<Result> ValidateNewReservationEntitiesAsync(
            Guid gamerId,
            Guid siteId,
            Guid? workstationId,
            Guid? zoneId,
            DateTime startTimeUtc,
            DateTime endTimeUtc,
            CancellationToken cancellationToken = default);
    }
}

using System;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Application.Reservations
{
    public class CreateReservationCommand : ICommand<ReservationResponseDto>
    {
        public Guid GamerId { get; set; }
        public Guid SiteId { get; set; }
        public Guid? WorkstationId { get; set; }
        public Guid? ZoneId { get; set; }
        public DateTime StartTimeUtc { get; set; }
        public DateTime EndTimeUtc { get; set; }
        public decimal? ReservedAmount { get; set; }
    }

    public class ConfirmReservationCommand : ICommand<ReservationResponseDto>
    {
        public Guid ReservationId { get; set; }
    }

    public class CancelReservationCommand : ICommand<ReservationResponseDto>
    {
        public Guid ReservationId { get; set; }
        public string? Reason { get; set; }
    }

    public class ActivateReservationCommand : ICommand<ReservationResponseDto>
    {
        public Guid ReservationId { get; set; }
    }

    public class GetReservationQuery : IQuery<ReservationResponseDto>
    {
        public Guid ReservationId { get; set; }
    }

    public class ValidateReservationQuery : IQuery<ReservationValidationResultDto>
    {
        public Guid? ReservationId { get; set; }
        public Guid? GamerId { get; set; }
        public Guid? SiteId { get; set; }
        public Guid? WorkstationId { get; set; }
        public DateTime? CheckTimeUtc { get; set; }
    }
}

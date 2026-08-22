using System;

namespace Sayra.Backend.Contracts
{
    public class CancelReservationRequestDto
    {
        public string? Reason { get; set; }
    }

    public class CreateReservationRequestDto
    {
        public Guid GamerId { get; set; }
        public Guid SiteId { get; set; }
        public Guid? WorkstationId { get; set; }
        public Guid? ZoneId { get; set; }
        public DateTime StartTimeUtc { get; set; }
        public DateTime EndTimeUtc { get; set; }
        public decimal? ReservedAmount { get; set; }
    }

    public class ReservationResponseDto
    {
        public Guid ReservationId { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid SiteId { get; set; }
        public Guid GamerId { get; set; }
        public Guid? WorkstationId { get; set; }
        public Guid? ZoneId { get; set; }
        public DateTime StartTimeUtc { get; set; }
        public DateTime EndTimeUtc { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal ReservedAmount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class ValidateReservationRequestDto
    {
        public Guid? ReservationId { get; set; }
        public Guid? GamerId { get; set; }
        public Guid? SiteId { get; set; }
        public Guid? WorkstationId { get; set; }
        public DateTime? CheckTimeUtc { get; set; }
    }

    public class ReservationValidationResultDto
    {
        public bool IsValid { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public ReservationResponseDto? Reservation { get; set; }

        public static ReservationValidationResultDto Valid(ReservationResponseDto? reservation = null, string reason = "Reservation is valid.")
        {
            return new ReservationValidationResultDto
            {
                IsValid = true,
                Code = "VALID",
                Reason = reason,
                Reservation = reservation
            };
        }

        public static ReservationValidationResultDto Invalid(string code, string reason, ReservationResponseDto? reservation = null)
        {
            return new ReservationValidationResultDto
            {
                IsValid = false,
                Code = code,
                Reason = reason,
                Reservation = reservation
            };
        }
    }
}

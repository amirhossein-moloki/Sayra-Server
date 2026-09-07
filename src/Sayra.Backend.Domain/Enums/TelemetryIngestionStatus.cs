namespace Sayra.Backend.Domain.Enums
{
    public enum TelemetryIngestionStatus
    {
        Accepted = 1,
        Rejected = 2,
        Duplicate = 3,
        Stale = 4,
        OutOfOrder = 5,
        IdentityMismatch = 6
    }
}

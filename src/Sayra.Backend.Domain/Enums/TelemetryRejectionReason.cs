namespace Sayra.Backend.Domain.Enums
{
    public enum TelemetryRejectionReason
    {
        None = 0,
        PayloadNull = 1,
        InvalidCpuRange = 2,
        InvalidRamRange = 3,
        InvalidUptimeRange = 4,
        InvalidGameCpuRange = 5,
        InvalidGameRamRange = 6,
        InvalidGameDurationRange = 7,
        GameNameExceedsMaxLength = 8,
        InvalidLaunchesRange = 9,
        InvalidCrashesRange = 10,
        InvalidRestartsRange = 11,
        MissingEventId = 12,
        MissingEventType = 13,
        IdentityMismatch = 14,
        TimestampInFuture = 15,
        TimestampExcessivelyOld = 16,
        DuplicateEvent = 17,
        StaleTelemetrySnapshot = 18,
        MalformedPayload = 19,
        OversizedPayload = 20,
        UnauthorizedContext = 21
    }
}

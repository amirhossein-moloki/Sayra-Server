namespace Sayra.Backend.Domain
{
    public enum UpdatePackageVerificationStatus
    {
        NotVerified = 0,
        Validating = 1,
        Valid = 2,
        Invalid = 3,
        Quarantined = 4
    }
}

namespace Sayra.Backend.Domain
{
    public enum UpdatePackageLifecycleState
    {
        Uploading = 0,
        Uploaded = 1,
        Validating = 2,
        Validated = 3,
        Signed = 4,
        Ready = 5,
        ValidationFailed = 6,
        Quarantined = 7
    }
}

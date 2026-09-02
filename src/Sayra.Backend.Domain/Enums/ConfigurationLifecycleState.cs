namespace Sayra.Backend.Domain
{
    public enum ConfigurationLifecycleState
    {
        Draft = 0,
        Validated = 1,
        Signed = 2,
        Published = 3,
        Active = 4,
        Superseded = 5,
        Revoked = 6
    }
}

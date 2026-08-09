namespace Sayra.Backend.Application.Abstractions.Transport
{
    public enum ConnectionLifecycleState
    {
        Connecting,
        Authenticating,
        Authenticated,
        Active,
        Disconnected
    }
}

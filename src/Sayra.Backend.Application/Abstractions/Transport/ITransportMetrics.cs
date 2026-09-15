namespace Sayra.Backend.Application.Abstractions.Transport
{
    public interface ITransportMetrics
    {
        void RecordConnectionAccepted();
        void RecordConnectionActiveDelta(int delta);
        void RecordConnectionRejected(string reason);
        void RecordAuthenticationConcurrencyDelta(int delta);
        void RecordAuthenticationRejected(string reason);
        void RecordSlowDisconnect(string reason);
        void RecordOversizedFrame(int frameSize, int limit);
        void RecordHttpRateLimitRejected(string endpoint, string policy);
    }
}

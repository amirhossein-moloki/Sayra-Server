using System.Threading;
using System.Threading.Tasks;

namespace Sayra.Backend.Application.Abstractions.Transport
{
    public interface ITcpServer
    {
        bool IsListening { get; }
        int ActiveConnectionsCount { get; }
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
    }
}

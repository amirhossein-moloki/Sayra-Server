using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Application.Abstractions.Communication
{
    public interface ICommunicationMessageDispatcher
    {
        Task DispatchAsync<TPayload>(CommunicationMessage<TPayload> message, CancellationToken cancellationToken = default);
        Task SendToConnectionAsync<TPayload>(string connectionId, CommunicationMessage<TPayload> message, CancellationToken cancellationToken = default);
        Task SendToWorkstationAsync<TPayload>(string pcId, CommunicationMessage<TPayload> message, CancellationToken cancellationToken = default);
    }
}

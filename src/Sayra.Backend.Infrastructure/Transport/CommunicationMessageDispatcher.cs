using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Communication;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class CommunicationMessageDispatcher : ICommunicationMessageDispatcher
    {
        private readonly ITcpConnectionRegistry _connectionRegistry;
        private readonly ILogger<CommunicationMessageDispatcher> _logger;

        public CommunicationMessageDispatcher(
            ITcpConnectionRegistry connectionRegistry,
            ILogger<CommunicationMessageDispatcher> logger)
        {
            _connectionRegistry = connectionRegistry ?? throw new ArgumentNullException(nameof(connectionRegistry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task DispatchAsync<TPayload>(CommunicationMessage<TPayload> message, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Dispatching message type {MessageType}, MessageId: {MessageId}", message.Metadata.MessageType, message.Metadata.MessageId);
            return Task.CompletedTask;
        }

        public async Task SendToConnectionAsync<TPayload>(string connectionId, CommunicationMessage<TPayload> message, CancellationToken cancellationToken = default)
        {
            var connection = _connectionRegistry.Get(connectionId);
            if (connection != null)
            {
                _logger.LogInformation("Sending message {MessageId} ({MessageType}) to ConnectionId {ConnectionId}", message.Metadata.MessageId, message.Metadata.MessageType, connectionId);
            }
            else
            {
                _logger.LogWarning("Cannot send message {MessageId}: Connection {ConnectionId} not found in registry.", message.Metadata.MessageId, connectionId);
            }
            await Task.CompletedTask;
        }

        public async Task SendToWorkstationAsync<TPayload>(string pcId, CommunicationMessage<TPayload> message, CancellationToken cancellationToken = default)
        {
            var connection = _connectionRegistry.GetByPcId(pcId);
            if (connection != null)
            {
                _logger.LogInformation("Sending message {MessageId} ({MessageType}) to PcId {PcId}", message.Metadata.MessageId, message.Metadata.MessageType, pcId);
            }
            else
            {
                _logger.LogWarning("Cannot send message {MessageId}: Workstation {PcId} not found in connection registry.", message.Metadata.MessageId, pcId);
            }
            await Task.CompletedTask;
        }
    }
}

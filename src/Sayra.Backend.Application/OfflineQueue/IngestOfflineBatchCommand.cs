using System;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Application.OfflineQueue
{
    public class IngestOfflineBatchCommand : ICommand<IngestOfflineBatchResult>
    {
        public string ConnectionId { get; }
        public string AuthenticatedPcId { get; }
        public OfflineBatchRequest BatchRequest { get; }

        public IngestOfflineBatchCommand(string connectionId, string authenticatedPcId, OfflineBatchRequest batchRequest)
        {
            ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
            AuthenticatedPcId = authenticatedPcId ?? throw new ArgumentNullException(nameof(authenticatedPcId));
            BatchRequest = batchRequest ?? throw new ArgumentNullException(nameof(batchRequest));
        }
    }

    public class IngestOfflineBatchResult
    {
        public OfflineBatchAcknowledgment Acknowledgment { get; }

        public IngestOfflineBatchResult(OfflineBatchAcknowledgment acknowledgment)
        {
            Acknowledgment = acknowledgment ?? throw new ArgumentNullException(nameof(acknowledgment));
        }
    }
}

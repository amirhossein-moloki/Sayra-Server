using System;

namespace Sayra.Backend.Application.Abstractions.Diagnostics
{
    public interface IWorkerMetrics
    {
        void RecordWorkerRun(string workerName, double durationSeconds, bool isSuccess);
        void RecordWorkerError(string workerName, string errorType);
        void RecordWorkerActiveState(string workerName, bool isActive);
        void RecordItemsProcessed(string workerName, long count);
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Sayra.Backend.Application.Resilience
{
    public interface IResiliencePipeline
    {
        Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            OperationRetrySafety safety,
            string dependencyName,
            string operationName,
            CancellationToken cancellationToken = default);

        Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            OperationRetrySafety safety,
            string dependencyName,
            string operationName,
            CancellationToken cancellationToken = default);

        ResilienceFailureCategory ClassifyException(Exception exception);
    }
}

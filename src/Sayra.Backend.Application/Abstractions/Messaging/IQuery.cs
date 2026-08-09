using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Abstractions.Messaging
{
    public interface IQuery<TResponse>
    {
    }

    public interface IQueryHandler<in TQuery, TResponse> where TQuery : IQuery<TResponse>
    {
        Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
    }
}

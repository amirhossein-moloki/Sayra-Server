using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Abstractions.Messaging
{
    public interface ICommand<TResponse>
    {
    }

    public interface ICommandHandler<in TCommand, TResponse> where TCommand : ICommand<TResponse>
    {
        Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
    }
}

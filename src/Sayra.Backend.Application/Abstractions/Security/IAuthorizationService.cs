using System.Threading;
using System.Threading.Tasks;

namespace Sayra.Backend.Application.Abstractions.Security
{
    public interface IAuthorizationService
    {
        Task<AuthorizationResult> AuthorizeAsync(
            UserPrincipal? principal,
            string permission,
            object? resource = null,
            CancellationToken cancellationToken = default);
    }
}

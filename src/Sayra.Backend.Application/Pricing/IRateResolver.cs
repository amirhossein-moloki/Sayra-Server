using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Application.Pricing
{
    public interface IRateResolver
    {
        Task<ResolvedRateResponseDto> ResolveRateAsync(ResolveRateRequestDto request, CancellationToken cancellationToken = default);
    }
}

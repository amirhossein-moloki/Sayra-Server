using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Configuration.Models;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Configuration
{
    public interface IConfigurationResolver
    {
        Task<Result<ConfigurationResolutionResult>> ResolveEffectiveConfigurationAsync(
            Guid workstationId,
            CancellationToken cancellationToken = default);
    }
}

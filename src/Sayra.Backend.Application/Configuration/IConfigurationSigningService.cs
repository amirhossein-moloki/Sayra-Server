using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Sayra.Backend.Application.Configuration
{
    public interface IConfigurationSigningService
    {
        Task<ConfigurationSignatureResult> SignPackageAsync(string content, string? keyId = null, CancellationToken cancellationToken = default);
        Task<ConfigurationVerificationResult> VerifyPackageAsync(string content, string hash, string signature, string keyId, CancellationToken cancellationToken = default);
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public class UpdateSignatureResult
    {
        public string Hash { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public string Algorithm { get; set; } = "RSA-SHA256";
        public string KeyId { get; set; } = string.Empty;
    }

    public class UpdateSignatureVerificationResult
    {
        public bool IsValid { get; private set; }
        public string? ErrorMessage { get; private set; }
        public string KeyId { get; private set; } = string.Empty;

        public static UpdateSignatureVerificationResult Success(string keyId) =>
            new() { IsValid = true, KeyId = keyId };

        public static UpdateSignatureVerificationResult Failure(string errorMessage, string keyId = "") =>
            new() { IsValid = false, ErrorMessage = errorMessage, KeyId = keyId };
    }

    public interface IUpdateSigningService
    {
        Task<UpdateSignatureResult> SignPackageAsync(UpdatePackage package, string? keyId = null, CancellationToken cancellationToken = default);
        Task<UpdateSignatureVerificationResult> VerifyPackageAsync(UpdatePackage package, CancellationToken cancellationToken = default);
        Task<UpdateSignatureVerificationResult> VerifyPackageAsync(UpdatePackage package, string signature, string keyId, CancellationToken cancellationToken = default);
    }
}

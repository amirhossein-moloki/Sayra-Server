namespace Sayra.Backend.Application.Configuration
{
    public class ConfigurationSignatureResult
    {
        public string Hash { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty; // Base64
        public string Algorithm { get; set; } = "RSA-SHA256";
        public string KeyId { get; set; } = string.Empty;
    }

    public class ConfigurationVerificationResult
    {
        public bool IsValid { get; set; }
        public string? FailureReason { get; set; }
        public string KeyId { get; set; } = string.Empty;

        public static ConfigurationVerificationResult Success(string keyId)
        {
            return new ConfigurationVerificationResult
            {
                IsValid = true,
                KeyId = keyId
            };
        }

        public static ConfigurationVerificationResult Failure(string reason, string keyId = "")
        {
            return new ConfigurationVerificationResult
            {
                IsValid = false,
                FailureReason = reason,
                KeyId = keyId
            };
        }
    }
}

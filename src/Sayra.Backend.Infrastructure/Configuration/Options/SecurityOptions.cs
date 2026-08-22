namespace Sayra.Backend.Infrastructure.Configuration.Options
{
    public class SecurityOptions
    {
        public const string SectionName = "Security";

        public string TokenSigningKey { get; set; } = string.Empty;
        public int TokenLifetimeMinutes { get; set; } = 60;
        public string PrivateKeyPem { get; set; } = string.Empty;
        public string PublicKeyPem { get; set; } = string.Empty;

        // Password Hashing & Policy Options
        public string PasswordHashAlgorithm { get; set; } = "Argon2id";
        public int ArgonDegreeOfParallelism { get; set; } = 2;
        public int ArgonMemorySizeKb { get; set; } = 19456; // 19MB
        public int ArgonIterations { get; set; } = 2;
        public int SaltSize { get; set; } = 16; // 128 bits
        public int KeySize { get; set; } = 32;  // 256 bits
        public int Pbkdf2Iterations { get; set; } = 10000;
        public int MaxPasswordLength { get; set; } = 128;
    }
}

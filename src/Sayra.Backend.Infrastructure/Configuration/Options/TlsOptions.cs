namespace Sayra.Backend.Infrastructure.Configuration.Options
{
    public class TlsOptions
    {
        public const string SectionName = "Tls";

        public bool RequireClientCertificate { get; set; } = false;
        public string CertificatePath { get; set; } = string.Empty;
        public string CertificatePassword { get; set; } = string.Empty;
    }
}

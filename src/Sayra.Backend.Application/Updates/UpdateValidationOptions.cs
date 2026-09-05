namespace Sayra.Backend.Application.Updates
{
    public class UpdateValidationOptions
    {
        public const string SectionName = "Updates";

        public long MaxArtifactSizeBytes { get; set; } = 524_288_000; // 500 MB default
    }
}

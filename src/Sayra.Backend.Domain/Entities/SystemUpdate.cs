using System;

namespace Sayra.Backend.Domain
{
    public class SystemUpdate : BaseEntity
    {
        public string Version { get; set; } = string.Empty;
        public string ChecksumSha256 { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public byte[]? DigitalSignature { get; set; }
        public DateTime ReleaseDate { get; set; } = DateTime.UtcNow;
        public bool IsMandatory { get; set; }
    }
}

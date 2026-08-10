using System;

namespace Sayra.Backend.Contracts
{
    public class UpdateManifest
    {
        public string Version { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public string PackageUrl { get; set; } = string.Empty;
        public string Checksum { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }
}

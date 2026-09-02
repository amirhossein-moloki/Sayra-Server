using System;
using System.Collections.Generic;

namespace Sayra.Backend.Application.Configuration.Models
{
    public class ConfigurationResolutionResult
    {
        public string EffectiveConfigurationJson { get; set; } = "{}";
        public string SchemaVersion { get; set; } = "1.0";
        public List<AppliedConfigurationSourceDto> AppliedSources { get; set; } = new();
        public List<ConfigurationFieldTraceDto> FieldTraces { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class AppliedConfigurationSourceDto
    {
        public string TargetType { get; set; } = string.Empty;
        public Guid TargetId { get; set; }
        public Guid PackageId { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public long VersionNumber { get; set; }
        public string Version { get; set; } = string.Empty;
        public Guid AssignmentId { get; set; }
    }

    public class ConfigurationFieldTraceDto
    {
        public string Path { get; set; } = string.Empty;
        public string TargetType { get; set; } = string.Empty;
        public Guid TargetId { get; set; }
        public long VersionNumber { get; set; }
    }
}

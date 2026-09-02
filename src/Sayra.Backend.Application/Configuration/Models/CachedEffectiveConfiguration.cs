using System;
using System.Collections.Generic;

namespace Sayra.Backend.Application.Configuration.Models
{
    public class CachedEffectiveConfiguration
    {
        public int CacheSchemaVersion { get; set; } = 1;
        public string SchemaVersion { get; set; } = "1.0";
        public string EffectiveConfigurationJson { get; set; } = "{}";
        public string ContentHash { get; set; } = string.Empty;
        public List<AppliedConfigurationSourceDto> AppliedSources { get; set; } = new();
        public List<ConfigurationFieldTraceDto> FieldTraces { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public DateTime CachedAtUtc { get; set; } = DateTime.UtcNow;
        public Dictionary<string, long> ScopeRevisions { get; set; } = new();
    }
}

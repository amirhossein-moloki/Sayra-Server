using System;
using System.Collections.Generic;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Application.Configuration
{
    public interface IConfigurationDeltaEngine
    {
        /// <summary>
        /// Applies a set of delta operations to a base normalized JSON configuration string.
        /// Performs the Apply-then-Normalize-then-Validate pipeline.
        /// </summary>
        string ApplyDelta(string baseNormalizedJson, IEnumerable<ConfigurationDelta> deltas);

        /// <summary>
        /// Applies a JSON string containing delta operations to a base normalized JSON configuration string.
        /// </summary>
        string ApplyDelta(string baseNormalizedJson, string deltaJson);

        /// <summary>
        /// Computes the delta operations required to transform baseNormalizedJson into targetNormalizedJson.
        /// </summary>
        List<ConfigurationDelta> ComputeDelta(string baseNormalizedJson, string targetNormalizedJson);
    }
}

using System;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Caching;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class ConfigurationCacheKeyTests
    {
        private const string Prefix = "sayra:config:v1:";

        [Fact]
        public void EffectiveConfigKey_IncludesOrganizationAndWorkstation()
        {
            var orgId = Guid.NewGuid();
            var wsId = Guid.NewGuid();

            var key = ConfigurationCacheKeyBuilder.GetEffectiveConfigKey(Prefix, orgId, wsId);

            Assert.Equal($"sayra:config:v1:effective:{orgId}:{wsId}", key);
        }

        [Fact]
        public void EffectiveConfigKey_EnforcesOrganizationIsolation()
        {
            var orgA = Guid.NewGuid();
            var orgB = Guid.NewGuid();
            var wsId = Guid.NewGuid();

            var keyA = ConfigurationCacheKeyBuilder.GetEffectiveConfigKey(Prefix, orgA, wsId);
            var keyB = ConfigurationCacheKeyBuilder.GetEffectiveConfigKey(Prefix, orgB, wsId);

            Assert.NotEqual(keyA, keyB);
            Assert.Contains(orgA.ToString(), keyA);
            Assert.Contains(orgB.ToString(), keyB);
        }

        [Fact]
        public void PublicationKey_IncludesOrganizationAndTarget()
        {
            var orgId = Guid.NewGuid();
            var targetId = Guid.NewGuid();

            var key = ConfigurationCacheKeyBuilder.GetPublicationKey(Prefix, orgId, targetId);

            Assert.Equal($"sayra:config:v1:publication:{orgId}:{targetId}", key);
        }

        [Fact]
        public void ScopeRevisionKey_FormatsCorrectlyForGlobalAndTargetedScopes()
        {
            var orgId = Guid.NewGuid();
            var targetId = Guid.NewGuid();

            var globalKey = ConfigurationCacheKeyBuilder.GetScopeRevisionKey(Prefix, orgId, ConfigurationTargetType.Global, null);
            var siteKey = ConfigurationCacheKeyBuilder.GetScopeRevisionKey(Prefix, orgId, ConfigurationTargetType.Site, targetId);

            Assert.Equal($"sayra:config:v1:rev:{orgId}:Global:global", globalKey);
            Assert.Equal($"sayra:config:v1:rev:{orgId}:Site:{targetId}", siteKey);
        }

        [Fact]
        public void StampedeLockKey_IncludesOrganizationAndWorkstation()
        {
            var orgId = Guid.NewGuid();
            var wsId = Guid.NewGuid();

            var key = ConfigurationCacheKeyBuilder.GetStampedeLockKey(Prefix, orgId, wsId);

            Assert.Equal($"sayra:config:v1:lock:{orgId}:{wsId}", key);
        }
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Infrastructure.Persistence;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class ConfigurationDomainAndPersistenceTests
    {
        #region ConfigurationVersion Tests

        [Theory]
        [InlineData(1, 0, 0, "1.0.0")]
        [InlineData(2, 5, 1, "2.5.1")]
        [InlineData(0, 1, 0, "0.1.0")]
        [InlineData(0, 0, 1, "0.0.1")]
        public void ConfigurationVersion_ValidConstruction_ShouldInitializeAndFormatString(int major, int minor, int patch, string expectedString)
        {
            var version = new ConfigurationVersion(major, minor, patch);
            Assert.Equal(major, version.Major);
            Assert.Equal(minor, version.Minor);
            Assert.Equal(patch, version.Patch);
            Assert.Equal(expectedString, version.ToString());
        }

        [Fact]
        public void ConfigurationVersion_AllZeroComponents_ShouldThrowInvalidDomainException()
        {
            var ex = Assert.Throws<InvalidDomainException>(() => new ConfigurationVersion(0, 0, 0));
            Assert.Equal("INVALID_VERSION", ex.ErrorCode);
        }

        [Theory]
        [InlineData(-1, 0, 0)]
        [InlineData(1, -1, 0)]
        [InlineData(1, 0, -1)]
        public void ConfigurationVersion_NegativeComponents_ShouldThrowInvalidDomainException(int major, int minor, int patch)
        {
            var ex = Assert.Throws<InvalidDomainException>(() => new ConfigurationVersion(major, minor, patch));
            Assert.Equal("INVALID_VERSION", ex.ErrorCode);
        }

        [Theory]
        [InlineData("1", 1, 0, 0)]
        [InlineData("2.3", 2, 3, 0)]
        [InlineData("4.5.6", 4, 5, 6)]
        public void ConfigurationVersion_Parse_ValidStrings_ShouldReturnInstance(string input, int expectedMajor, int expectedMinor, int expectedPatch)
        {
            var version = ConfigurationVersion.Parse(input);
            Assert.NotNull(version);
            Assert.Equal(expectedMajor, version.Major);
            Assert.Equal(expectedMinor, version.Minor);
            Assert.Equal(expectedPatch, version.Patch);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("abc")]
        [InlineData("1.2.3.4")]
        [InlineData("-1.0.0")]
        public void ConfigurationVersion_Parse_InvalidStrings_ShouldThrowInvalidDomainException(string input)
        {
            var ex = Assert.Throws<InvalidDomainException>(() => ConfigurationVersion.Parse(input));
            Assert.Equal("INVALID_VERSION", ex.ErrorCode);
        }

        [Fact]
        public void ConfigurationVersion_ComparisonAndEquality_ShouldBehaveDeterministically()
        {
            var v1 = new ConfigurationVersion(1, 0, 0);
            var v1Copy = new ConfigurationVersion(1, 0, 0);
            var v2 = new ConfigurationVersion(1, 1, 0);
            var v3 = new ConfigurationVersion(2, 0, 0);

            Assert.Equal(v1, v1Copy);
            Assert.True(v1 == v1Copy);
            Assert.False(v1 != v1Copy);
            Assert.True(v1 < v2);
            Assert.True(v2 < v3);
            Assert.True(v3 > v1);
            Assert.True(v1 <= v1Copy);
            Assert.True(v1 >= v1Copy);
            Assert.Equal(0, v1.CompareTo(v1Copy));
            Assert.True(v1.CompareTo(v2) < 0);
        }

        #endregion

        #region ConfigurationStatus & Enum Tests

        [Theory]
        [InlineData("draft", ConfigurationStatus.DRAFT)]
        [InlineData("VALIDATED", ConfigurationStatus.VALIDATED)]
        [InlineData("signed", ConfigurationStatus.SIGNED)]
        [InlineData("PUBLISHED", ConfigurationStatus.PUBLISHED)]
        [InlineData("active", ConfigurationStatus.ACTIVE)]
        [InlineData("SUPERSEDED", ConfigurationStatus.SUPERSEDED)]
        [InlineData("revoked", ConfigurationStatus.REVOKED)]
        public void ConfigurationStatus_Parse_ShouldNormalizeAndReturnStatus(string input, ConfigurationStatus expected)
        {
            var parsed = ConfigurationStatusExtensions.ParseStatus(input);
            Assert.Equal(expected, parsed);
        }

        [Fact]
        public void ConfigurationStatus_Parse_InvalidString_ShouldThrowInvalidDomainException()
        {
            var ex = Assert.Throws<InvalidDomainException>(() => ConfigurationStatusExtensions.ParseStatus("UNKNOWN_STATUS"));
            Assert.Equal("INVALID_CONFIGURATION_STATUS", ex.ErrorCode);
        }

        [Fact]
        public void ConfigurationStatus_IsImmutable_ShouldReturnTrueOnlyForPublishedActiveSupersededRevoked()
        {
            Assert.False(ConfigurationStatus.DRAFT.IsImmutable());
            Assert.False(ConfigurationStatus.VALIDATED.IsImmutable());
            Assert.False(ConfigurationStatus.SIGNED.IsImmutable());

            Assert.True(ConfigurationStatus.PUBLISHED.IsImmutable());
            Assert.True(ConfigurationStatus.ACTIVE.IsImmutable());
            Assert.True(ConfigurationStatus.SUPERSEDED.IsImmutable());
            Assert.True(ConfigurationStatus.REVOKED.IsImmutable());
        }

        #endregion

        #region ConfigurationTarget Tests

        [Fact]
        public void ConfigurationTarget_CreateGlobal_ShouldSetGlobalDefaults()
        {
            var target = ConfigurationTarget.CreateGlobal("Global Policy");
            Assert.Equal(ConfigurationTargetType.GLOBAL, target.TargetType);
            Assert.Equal("GLOBAL", target.TargetIdentifier);
            Assert.Equal("Global Policy", target.Description);
        }

        [Fact]
        public void ConfigurationTarget_CreateSiteTarget_ValidParameters_ShouldNormalizeIdentifier()
        {
            var siteId = Guid.NewGuid();
            var target = ConfigurationTarget.CreateSiteTarget(siteId, " site-alpha ", "Site A policy");
            Assert.Equal(ConfigurationTargetType.SITE, target.TargetType);
            Assert.Equal("SITE-ALPHA", target.TargetIdentifier);
            Assert.Equal(siteId, target.SiteEntityId);
        }

        [Fact]
        public void ConfigurationTarget_CreateWorkstationTarget_ValidParameters_ShouldNormalizePcId()
        {
            var pcGuid = Guid.NewGuid();
            var target = ConfigurationTarget.CreateWorkstationTarget(pcGuid, " pc-front-01 ", "Workstation Policy");
            Assert.Equal(ConfigurationTargetType.WORKSTATION, target.TargetType);
            Assert.Equal("PC-FRONT-01", target.TargetIdentifier);
            Assert.Equal(pcGuid, target.WorkstationEntityId);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void ConfigurationTarget_CreateSiteTarget_EmptyIdentifier_ShouldThrowInvalidDomainException(string siteIdentifier)
        {
            var ex = Assert.Throws<InvalidDomainException>(() => ConfigurationTarget.CreateSiteTarget(Guid.NewGuid(), siteIdentifier));
            Assert.Equal("INVALID_TARGET_IDENTIFIER", ex.ErrorCode);
        }

        #endregion

        #region ConfigurationPackage Aggregate Root Tests

        [Fact]
        public void ConfigurationPackage_CreateFull_ValidParameters_ShouldInitializeDraftPackage()
        {
            var version = new ConfigurationVersion(1, 0, 0);
            var package = ConfigurationPackage.CreateFull("DEFAULT_CONFIG", "Default Global Config", version, "{\"setting\":1}", "admin");

            Assert.NotNull(package);
            Assert.Equal("DEFAULT_CONFIG", package.PackageId);
            Assert.Equal("Default Global Config", package.Name);
            Assert.Equal(version, package.Version);
            Assert.Null(package.BaseVersion);
            Assert.Equal(ConfigurationPayloadType.FULL, package.PayloadType);
            Assert.Equal(ConfigurationStatus.DRAFT, package.Status);
            Assert.Equal("{\"setting\":1}", package.Content);
            Assert.Equal("admin", package.CreatedBy);
        }

        [Fact]
        public void ConfigurationPackage_CreateDelta_ValidBaseAndTargetVersion_ShouldInitializeDeltaPackage()
        {
            var baseVer = new ConfigurationVersion(1, 0, 0);
            var targetVer = new ConfigurationVersion(1, 1, 0);
            var package = ConfigurationPackage.CreateDelta("DEFAULT_CONFIG", "Default Config Patch", targetVer, baseVer, "{\"patch\":true}", "admin");

            Assert.NotNull(package);
            Assert.Equal(ConfigurationPayloadType.DELTA, package.PayloadType);
            Assert.Equal(baseVer, package.BaseVersion);
            Assert.Equal(targetVer, package.Version);
        }

        [Fact]
        public void ConfigurationPackage_CreateDelta_BaseVersionGreaterOrEqualToVersion_ShouldThrowInvalidDomainException()
        {
            var ver1 = new ConfigurationVersion(1, 0, 0);
            var ver2 = new ConfigurationVersion(1, 0, 0);

            var ex = Assert.Throws<InvalidDomainException>(() =>
                ConfigurationPackage.CreateDelta("DEFAULT_CONFIG", "Patch", ver1, ver2, "{}", "admin"));

            Assert.Equal("INVALID_BASE_VERSION", ex.ErrorCode);
        }

        [Fact]
        public void ConfigurationPackage_StateTransitions_Validate_Sign_Publish_ShouldUpdateStatusAndMetadata()
        {
            var package = ConfigurationPackage.CreateFull("PKG-01", "Base Package", new ConfigurationVersion(1, 0, 0), "{\"key\":\"val\"}", "author");

            // Validate
            package.ValidatePackage();
            Assert.Equal(ConfigurationStatus.VALIDATED, package.Status);

            // Sign
            package.Sign("SIG-123456", "SecurityOfficer");
            Assert.Equal(ConfigurationStatus.SIGNED, package.Status);
            Assert.Equal("SIG-123456", package.Signature);
            Assert.Equal("SecurityOfficer", package.SignerIdentity);
            Assert.NotNull(package.SignedAt);

            // Publish
            package.Publish("PublisherAdmin");
            Assert.Equal(ConfigurationStatus.PUBLISHED, package.Status);
            Assert.Equal("PublisherAdmin", package.PublishedBy);
            Assert.NotNull(package.PublishedAt);
        }

        [Fact]
        public void ConfigurationPackage_PublishedPackage_AttemptSetContent_ShouldThrowInvalidDomainException()
        {
            var package = ConfigurationPackage.CreateFull("PKG-02", "Immutable Package", new ConfigurationVersion(1, 0, 0), "{\"key\":\"val\"}", "author");
            package.Publish("Admin");

            Assert.True(package.Status.IsImmutable());

            var ex = Assert.Throws<InvalidDomainException>(() => package.SetContent("{\"modified\":true}"));
            Assert.Equal("PACKAGE_IMMUTABLE", ex.ErrorCode);
        }

        #endregion

        #region ConfigurationAssignment & Publication Tests

        [Fact]
        public void ConfigurationAssignment_Create_ValidParameters_ShouldInitializeAssignment()
        {
            var pkgId = Guid.NewGuid();
            var targetId = Guid.NewGuid();

            var assignment = ConfigurationAssignment.Create(pkgId, targetId, "AdminUser", priority: 10);

            Assert.NotNull(assignment);
            Assert.Equal(pkgId, assignment.ConfigurationPackageId);
            Assert.Equal(targetId, assignment.ConfigurationTargetId);
            Assert.Equal("AdminUser", assignment.AssignedBy);
            Assert.Equal(10, assignment.Priority);
            Assert.True(assignment.IsActive);
        }

        [Fact]
        public void ConfigurationAssignment_InvalidDates_ShouldThrowInvalidDomainException()
        {
            var pkgId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var ex = Assert.Throws<InvalidDomainException>(() =>
                ConfigurationAssignment.Create(pkgId, targetId, "Admin", effectiveFrom: now, effectiveTo: now.AddMinutes(-5)));

            Assert.Equal("INVALID_ASSIGNMENT_DATES", ex.ErrorCode);
        }

        [Fact]
        public void ConfigurationPublication_Create_ShouldInitializePublicationRecord()
        {
            var pkgId = Guid.NewGuid();
            var pub = ConfigurationPublication.Create(pkgId, "ReleaseManager", notes: "Production Release v1.0");

            Assert.NotNull(pub);
            Assert.Equal(pkgId, pub.ConfigurationPackageId);
            Assert.Equal("ReleaseManager", pub.PublishedBy);
            Assert.Equal(ConfigurationStatus.PUBLISHED, pub.Status);
            Assert.Equal("Production Release v1.0", pub.Notes);
        }

        #endregion

        #region Persistence Integration Tests with DbContext

        [Fact]
        public async Task DbContext_ShouldPersistAndRetrieve_ConfigurationEntities_WithJsonbAndConstraints()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using (var dbContext = new ApplicationDbContext(options))
            {
                // Create Target
                var target = ConfigurationTarget.CreateGlobal("Global Policy Scope");
                await dbContext.ConfigurationTargets.AddAsync(target);

                // Create Package
                var version = new ConfigurationVersion(1, 0, 0);
                var package = ConfigurationPackage.CreateFull("GLOBAL_PKG", "Global Base Package", version, "{\"networkTimeout\":30}", "SystemAdmin");
                package.Publish("ReleaseBot");
                await dbContext.ConfigurationPackages.AddAsync(package);

                await dbContext.SaveChangesAsync();

                // Create Assignment
                var assignment = ConfigurationAssignment.Create(package.Id, target.Id, "PolicyEngine", priority: 100);
                await dbContext.ConfigurationAssignments.AddAsync(assignment);

                // Create Publication
                var publication = ConfigurationPublication.Create(package.Id, "ReleaseBot", target.Id, "Initial Release");
                await dbContext.ConfigurationPublications.AddAsync(publication);

                await dbContext.SaveChangesAsync();
            }

            using (var readContext = new ApplicationDbContext(options))
            {
                var fetchedPackage = await readContext.ConfigurationPackages.FirstOrDefaultAsync(p => p.PackageId == "GLOBAL_PKG");
                Assert.NotNull(fetchedPackage);
                Assert.Equal("Global Base Package", fetchedPackage.Name);
                Assert.Equal(new ConfigurationVersion(1, 0, 0), fetchedPackage.Version);
                Assert.Equal(ConfigurationStatus.PUBLISHED, fetchedPackage.Status);
                Assert.Equal("{\"networkTimeout\":30}", fetchedPackage.Content);

                var fetchedTarget = await readContext.ConfigurationTargets.FirstOrDefaultAsync(t => t.TargetType == ConfigurationTargetType.GLOBAL);
                Assert.NotNull(fetchedTarget);
                Assert.Equal("GLOBAL", fetchedTarget.TargetIdentifier);

                var fetchedAssignment = await readContext.ConfigurationAssignments
                    .Include(a => a.Package)
                    .Include(a => a.Target)
                    .FirstOrDefaultAsync(a => a.ConfigurationPackageId == fetchedPackage.Id);

                Assert.NotNull(fetchedAssignment);
                Assert.NotNull(fetchedAssignment.Package);
                Assert.NotNull(fetchedAssignment.Target);
                Assert.Equal(100, fetchedAssignment.Priority);

                var fetchedPublication = await readContext.ConfigurationPublications.FirstOrDefaultAsync(p => p.ConfigurationPackageId == fetchedPackage.Id);
                Assert.NotNull(fetchedPublication);
                Assert.Equal("ReleaseBot", fetchedPublication.PublishedBy);
            }
        }

        #endregion
    }
}

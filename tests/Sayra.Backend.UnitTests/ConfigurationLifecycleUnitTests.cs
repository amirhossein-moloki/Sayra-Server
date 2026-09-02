using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Infrastructure.Security;
using Sayra.Backend.Shared;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class ConfigurationLifecycleUnitTests
    {
        private static string GetValidConfigPayload(int port = 5000)
        {
            return $@"{{
              ""version"": ""1.0"",
              ""server"": {{
                ""ipAddress"": ""192.168.1.100"",
                ""port"": {port}
              }},
              ""discovery"": {{
                ""enabled"": true,
                ""port"": 37020
              }},
              ""heartbeat"": {{
                ""intervalSeconds"": 10,
                ""timeoutSeconds"": 30
              }},
              ""kiosk"": {{
                ""enabled"": true,
                ""allowShellEscape"": false,
                ""autoLoginGamer"": false,
                ""idleTimeoutMinutes"": 15
              }},
              ""localization"": {{
                ""culture"": ""en-US"",
                ""timeZone"": ""UTC""
              }},
              ""security"": {{
                ""enableSsl"": true,
                ""requireEncryption"": true,
                ""maxFailedAttempts"": 5
              }}
            }}";
        }

        private static ApplicationDbContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            return new ApplicationDbContext(options);
        }

        private static IConfigurationSigningService CreateSigningService(ApplicationDbContext dbContext)
        {
            var repo = new ConfigurationKeyRegistryRepository(dbContext);
            var keyProvider = new SigningPrivateKeyProvider(Microsoft.Extensions.Options.Options.Create(new Sayra.Backend.Infrastructure.Configuration.Options.SecurityOptions()));
            var keyRegistry = new ConfigurationKeyRegistry(repo, keyProvider, dbContext);

            var serializer = new CanonicalConfigurationSerializer();
            var hashService = new ConfigurationHashService(serializer);
            var cryptoService = new CryptographicService();

            return new ConfigurationSigningService(serializer, hashService, keyRegistry, keyProvider, cryptoService);
        }

        // -------------------------------------------------------------------
        // 1. Lifecycle State Transition Matrix Tests
        // -------------------------------------------------------------------
        [Theory]
        [InlineData(ConfigurationLifecycleState.Draft, ConfigurationLifecycleState.Validated)]
        [InlineData(ConfigurationLifecycleState.Validated, ConfigurationLifecycleState.Signed)]
        [InlineData(ConfigurationLifecycleState.Signed, ConfigurationLifecycleState.Published)]
        [InlineData(ConfigurationLifecycleState.Published, ConfigurationLifecycleState.Active)]
        [InlineData(ConfigurationLifecycleState.Active, ConfigurationLifecycleState.Superseded)]
        [InlineData(ConfigurationLifecycleState.Draft, ConfigurationLifecycleState.Revoked)]
        [InlineData(ConfigurationLifecycleState.Validated, ConfigurationLifecycleState.Revoked)]
        [InlineData(ConfigurationLifecycleState.Signed, ConfigurationLifecycleState.Revoked)]
        [InlineData(ConfigurationLifecycleState.Published, ConfigurationLifecycleState.Revoked)]
        [InlineData(ConfigurationLifecycleState.Active, ConfigurationLifecycleState.Revoked)]
        [InlineData(ConfigurationLifecycleState.Superseded, ConfigurationLifecycleState.Revoked)]
        public void LifecycleValidator_ValidTransitions_Succeed(ConfigurationLifecycleState current, ConfigurationLifecycleState next)
        {
            Assert.True(ConfigurationLifecycleValidator.IsValidTransition(current, next));
            ConfigurationLifecycleValidator.ValidateTransition(current, next); // Should not throw
        }

        [Theory]
        [InlineData(ConfigurationLifecycleState.Draft, ConfigurationLifecycleState.Active)]
        [InlineData(ConfigurationLifecycleState.Draft, ConfigurationLifecycleState.Published)]
        [InlineData(ConfigurationLifecycleState.Validated, ConfigurationLifecycleState.Active)]
        [InlineData(ConfigurationLifecycleState.Revoked, ConfigurationLifecycleState.Active)]
        [InlineData(ConfigurationLifecycleState.Revoked, ConfigurationLifecycleState.Published)]
        [InlineData(ConfigurationLifecycleState.Superseded, ConfigurationLifecycleState.Active)]
        public void LifecycleValidator_IllegalTransitions_ThrowInvalidDomainException(ConfigurationLifecycleState current, ConfigurationLifecycleState next)
        {
            Assert.False(ConfigurationLifecycleValidator.IsValidTransition(current, next));
            var ex = Assert.Throws<InvalidDomainException>(() => ConfigurationLifecycleValidator.ValidateTransition(current, next));
            Assert.Equal("INVALID_LIFECYCLE_TRANSITION", ex.ErrorCode);
            Assert.Contains(current.ToString(), ex.Message);
            Assert.Contains(next.ToString(), ex.Message);
        }

        // -------------------------------------------------------------------
        // 2. Publication Preconditions Tests
        // -------------------------------------------------------------------
        [Fact]
        public async Task PublishConfiguration_UnsignedPackage_Rejected()
        {
            using var dbContext = CreateInMemoryDbContext();
            var pkgRepo = new ConfigurationPackageRepository(dbContext);
            var targetRepo = new ConfigurationTargetRepository(dbContext);
            var assignRepo = new ConfigurationAssignmentRepository(dbContext);
            var pubRepo = new ConfigurationPublicationRepository(dbContext);
            var signingService = CreateSigningService(dbContext);
            var validator = new ConfigurationValidatorService();

            var orgId = Guid.NewGuid();
            var target = ConfigurationTarget.CreateGlobal(orgId);
            await targetRepo.AddAsync(target);

            var unsignedPkg = ConfigurationPackage.CreateFull("default", 1, GetValidConfigPayload());
            await pkgRepo.AddAsync(unsignedPkg);
            await dbContext.SaveChangesAsync();

            var handler = new PublishConfigurationCommandHandler(pkgRepo, targetRepo, assignRepo, pubRepo, signingService, validator, dbContext);

            var result = await handler.HandleAsync(new PublishConfigurationCommand(unsignedPkg.Id, target.Id));

            Assert.False(result.IsSuccess);
            Assert.Equal("ConfigurationNotSigned", result.ErrorCode);
        }

        [Fact]
        public async Task PublishConfiguration_CorruptedSignature_Rejected()
        {
            using var dbContext = CreateInMemoryDbContext();
            var pkgRepo = new ConfigurationPackageRepository(dbContext);
            var targetRepo = new ConfigurationTargetRepository(dbContext);
            var assignRepo = new ConfigurationAssignmentRepository(dbContext);
            var pubRepo = new ConfigurationPublicationRepository(dbContext);
            var signingService = CreateSigningService(dbContext);
            var validator = new ConfigurationValidatorService();

            var orgId = Guid.NewGuid();
            var target = ConfigurationTarget.CreateGlobal(orgId);
            await targetRepo.AddAsync(target);

            var payload = GetValidConfigPayload();
            var signedPkg = ConfigurationPackage.CreateFull("default", 1, payload);
            var signRes = await signingService.SignPackageAsync(payload);

            // Tamper signature
            signedPkg.SetCryptographicSignature(signRes.Hash, "INVALID_TAMPERED_SIGNATURE_BASE64==", signRes.Algorithm, signRes.KeyId);
            await pkgRepo.AddAsync(signedPkg);
            await dbContext.SaveChangesAsync();

            var handler = new PublishConfigurationCommandHandler(pkgRepo, targetRepo, assignRepo, pubRepo, signingService, validator, dbContext);

            var result = await handler.HandleAsync(new PublishConfigurationCommand(signedPkg.Id, target.Id));

            Assert.False(result.IsSuccess);
            Assert.Equal("InvalidConfigurationSignature", result.ErrorCode);
        }

        [Fact]
        public async Task PublishConfiguration_ValidSignedPackage_SuccessfullyPublished()
        {
            using var dbContext = CreateInMemoryDbContext();
            var pkgRepo = new ConfigurationPackageRepository(dbContext);
            var targetRepo = new ConfigurationTargetRepository(dbContext);
            var assignRepo = new ConfigurationAssignmentRepository(dbContext);
            var pubRepo = new ConfigurationPublicationRepository(dbContext);
            var signingService = CreateSigningService(dbContext);
            var validator = new ConfigurationValidatorService();

            var orgId = Guid.NewGuid();
            var target = ConfigurationTarget.CreateGlobal(orgId);
            await targetRepo.AddAsync(target);

            var payload = GetValidConfigPayload();
            var signedPkg = ConfigurationPackage.CreateFull("default", 1, payload);
            var signRes = await signingService.SignPackageAsync(payload);
            signedPkg.SetCryptographicSignature(signRes.Hash, signRes.Signature, signRes.Algorithm, signRes.KeyId);

            await pkgRepo.AddAsync(signedPkg);
            await dbContext.SaveChangesAsync();

            var handler = new PublishConfigurationCommandHandler(pkgRepo, targetRepo, assignRepo, pubRepo, signingService, validator, dbContext);

            var result = await handler.HandleAsync(new PublishConfigurationCommand(signedPkg.Id, target.Id, Actor: "admin@sayra.dev"));

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.NotNull(result.Value);
            Assert.Equal("Published", result.Value.Status);
            Assert.Equal("admin@sayra.dev", result.Value.IssuedBy);
            Assert.NotNull(result.Value.PublishedAt);
        }

        // -------------------------------------------------------------------
        // 3. Activation & Supersession Tests
        // -------------------------------------------------------------------
        [Fact]
        public async Task ActivateConfiguration_AtomicSupersessionOfPreviousActivePublication()
        {
            using var dbContext = CreateInMemoryDbContext();
            var pkgRepo = new ConfigurationPackageRepository(dbContext);
            var targetRepo = new ConfigurationTargetRepository(dbContext);
            var assignRepo = new ConfigurationAssignmentRepository(dbContext);
            var pubRepo = new ConfigurationPublicationRepository(dbContext);
            var signingService = CreateSigningService(dbContext);
            var validator = new ConfigurationValidatorService();

            var orgId = Guid.NewGuid();
            var target = ConfigurationTarget.CreateGlobal(orgId);
            await targetRepo.AddAsync(target);

            // Create v1
            var pkg1 = ConfigurationPackage.CreateFull("default", 1, GetValidConfigPayload(5000));
            var sign1 = await signingService.SignPackageAsync(pkg1.Content);
            pkg1.SetCryptographicSignature(sign1.Hash, sign1.Signature, sign1.Algorithm, sign1.KeyId);
            await pkgRepo.AddAsync(pkg1);

            // Create v2
            var pkg2 = ConfigurationPackage.CreateFull("default", 2, GetValidConfigPayload(5001));
            var sign2 = await signingService.SignPackageAsync(pkg2.Content);
            pkg2.SetCryptographicSignature(sign2.Hash, sign2.Signature, sign2.Algorithm, sign2.KeyId);
            await pkgRepo.AddAsync(pkg2);

            await dbContext.SaveChangesAsync();

            var publishHandler = new PublishConfigurationCommandHandler(pkgRepo, targetRepo, assignRepo, pubRepo, signingService, validator, dbContext);
            var activateHandler = new ActivateConfigurationCommandHandler(pubRepo, pkgRepo, signingService, dbContext);

            // Publish and activate v1
            var pub1Res = await publishHandler.HandleAsync(new PublishConfigurationCommand(pkg1.Id, target.Id));
            Assert.True(pub1Res.IsSuccess);

            var act1Res = await activateHandler.HandleAsync(new ActivateConfigurationCommand(pub1Res.Value!.Id));
            Assert.True(act1Res.IsSuccess);
            Assert.Equal("Active", act1Res.Value!.Status);

            // Publish and activate v2
            var pub2Res = await publishHandler.HandleAsync(new PublishConfigurationCommand(pkg2.Id, target.Id));
            Assert.True(pub2Res.IsSuccess);

            var act2Res = await activateHandler.HandleAsync(new ActivateConfigurationCommand(pub2Res.Value!.Id));
            Assert.True(act2Res.IsSuccess);
            Assert.Equal("Active", act2Res.Value!.Status);

            // Verify v1 is now Superseded
            var v1Updated = await pubRepo.GetByIdAsync(pub1Res.Value.Id);
            Assert.NotNull(v1Updated);
            Assert.Equal(ConfigurationLifecycleState.Superseded, v1Updated.Status);
            Assert.Equal(act2Res.Value.Id, v1Updated.SupersededByPublicationId);
            Assert.NotNull(v1Updated.SupersededAt);

            // Verify target has exactly one active publication (v2)
            var currentActive = await pubRepo.GetActivePublicationForTargetAsync(target.Id);
            Assert.NotNull(currentActive);
            Assert.Equal(pub2Res.Value.Id, currentActive.Id);
            Assert.Equal(2, currentActive.VersionNumber);
        }

        // -------------------------------------------------------------------
        // 4. Revocation Rules Tests
        // -------------------------------------------------------------------
        [Fact]
        public async Task RevokePublication_ActivePublication_RevokesAndDeactivatesPackage()
        {
            using var dbContext = CreateInMemoryDbContext();
            var pkgRepo = new ConfigurationPackageRepository(dbContext);
            var targetRepo = new ConfigurationTargetRepository(dbContext);
            var assignRepo = new ConfigurationAssignmentRepository(dbContext);
            var pubRepo = new ConfigurationPublicationRepository(dbContext);
            var signingService = CreateSigningService(dbContext);
            var validator = new ConfigurationValidatorService();

            var orgId = Guid.NewGuid();
            var target = ConfigurationTarget.CreateGlobal(orgId);
            await targetRepo.AddAsync(target);

            var pkg = ConfigurationPackage.CreateFull("default", 1, GetValidConfigPayload());
            var sign = await signingService.SignPackageAsync(pkg.Content);
            pkg.SetCryptographicSignature(sign.Hash, sign.Signature, sign.Algorithm, sign.KeyId);
            await pkgRepo.AddAsync(pkg);
            await dbContext.SaveChangesAsync();

            var publishHandler = new PublishConfigurationCommandHandler(pkgRepo, targetRepo, assignRepo, pubRepo, signingService, validator, dbContext);
            var activateHandler = new ActivateConfigurationCommandHandler(pubRepo, pkgRepo, signingService, dbContext);
            var revokeHandler = new RevokeConfigurationCommandHandler(pubRepo, pkgRepo, dbContext);

            var pubRes = await publishHandler.HandleAsync(new PublishConfigurationCommand(pkg.Id, target.Id));
            var actRes = await activateHandler.HandleAsync(new ActivateConfigurationCommand(pubRes.Value!.Id));

            // Revoke active publication
            var revokeRes = await revokeHandler.HandleAsync(new RevokeConfigurationCommand(actRes.Value!.Id, "Security Breach Emergency Revocation", "secops@sayra.dev"));

            Assert.True(revokeRes.IsSuccess, revokeRes.ErrorMessage);
            Assert.Equal("Revoked", revokeRes.Value!.Status);
            Assert.Equal("Security Breach Emergency Revocation", revokeRes.Value.RevocationReason);
            Assert.Equal("secops@sayra.dev", revokeRes.Value.RevokedBy);

            // Verify cannot re-activate revoked publication
            var reActivateRes = await activateHandler.HandleAsync(new ActivateConfigurationCommand(actRes.Value.Id));
            Assert.False(reActivateRes.IsSuccess);
            Assert.Equal("ConfigurationRevoked", reActivateRes.ErrorCode);

            // Verify active publication for target is null
            var currentActive = await pubRepo.GetActivePublicationForTargetAsync(target.Id);
            Assert.Null(currentActive);
        }

        // -------------------------------------------------------------------
        // 5. Rollback Invariant & Cryptographic Integrity Tests
        // -------------------------------------------------------------------
        [Fact]
        public async Task RollbackConfiguration_CreatesNewImmutablePackageAndPublication_PreservesSourceAndFailed()
        {
            using var dbContext = CreateInMemoryDbContext();
            var pkgRepo = new ConfigurationPackageRepository(dbContext);
            var targetRepo = new ConfigurationTargetRepository(dbContext);
            var assignRepo = new ConfigurationAssignmentRepository(dbContext);
            var pubRepo = new ConfigurationPublicationRepository(dbContext);
            var signingService = CreateSigningService(dbContext);
            var validator = new ConfigurationValidatorService();
            var normalizer = new ConfigurationNormalizer(validator);
            var deltaEngine = new ConfigurationDeltaEngine(normalizer, validator);

            var orgId = Guid.NewGuid();
            var target = ConfigurationTarget.CreateGlobal(orgId);
            await targetRepo.AddAsync(target);

            // v1 GOOD
            var v1Payload = GetValidConfigPayload(5000);
            var pkg1 = ConfigurationPackage.CreateFull("default", 1, v1Payload);
            var sign1 = await signingService.SignPackageAsync(v1Payload);
            pkg1.SetCryptographicSignature(sign1.Hash, sign1.Signature, sign1.Algorithm, sign1.KeyId);
            await pkgRepo.AddAsync(pkg1);

            // v2 BAD
            var v2Payload = GetValidConfigPayload(9999);
            var pkg2 = ConfigurationPackage.CreateFull("default", 2, v2Payload);
            var sign2 = await signingService.SignPackageAsync(v2Payload);
            pkg2.SetCryptographicSignature(sign2.Hash, sign2.Signature, sign2.Algorithm, sign2.KeyId);
            await pkgRepo.AddAsync(pkg2);

            await dbContext.SaveChangesAsync();

            var publishHandler = new PublishConfigurationCommandHandler(pkgRepo, targetRepo, assignRepo, pubRepo, signingService, validator, dbContext);
            var activateHandler = new ActivateConfigurationCommandHandler(pubRepo, pkgRepo, signingService, dbContext);
            var rollbackHandler = new RollbackConfigurationCommandHandler(targetRepo, pkgRepo, assignRepo, pubRepo, signingService, validator, deltaEngine, dbContext);

            // Activate v1 then v2
            var pub1 = await publishHandler.HandleAsync(new PublishConfigurationCommand(pkg1.Id, target.Id));
            await activateHandler.HandleAsync(new ActivateConfigurationCommand(pub1.Value!.Id));

            var pub2 = await publishHandler.HandleAsync(new PublishConfigurationCommand(pkg2.Id, target.Id));
            var act2 = await activateHandler.HandleAsync(new ActivateConfigurationCommand(pub2.Value!.Id));

            // Execute Rollback from v2 to v1
            var rollbackRes = await rollbackHandler.HandleAsync(new RollbackConfigurationCommand(
                ConfigurationTargetId: target.Id,
                KnownGoodVersionNumber: 1,
                FailedVersionNumber: 2,
                Reason: "v2 caused port conflict in production",
                Actor: "incident-commander@sayra.dev",
                CorrelationId: "INC-2026-0826"));

            Assert.True(rollbackRes.IsSuccess, rollbackRes.ErrorMessage);
            Assert.NotNull(rollbackRes.Value);

            // 1. Rollback produces a NEW version v3
            Assert.Equal(3, rollbackRes.Value.VersionNumber);
            Assert.Equal("Active", rollbackRes.Value.Status);
            Assert.True(rollbackRes.Value.IsRollback);
            Assert.Equal(1, rollbackRes.Value.SourceVersionNumber);
            Assert.Equal(2, rollbackRes.Value.FailedVersionNumber);
            Assert.Equal("v2 caused port conflict in production", rollbackRes.Value.Notes);

            // 2. Rollback package v3 exists as a distinct immutable package with fresh signature and hash
            var pkg3 = await pkgRepo.GetByVersionNumberAsync("default", 3);
            Assert.NotNull(pkg3);
            Assert.NotEqual(pkg1.Id, pkg3.Id);
            Assert.NotNull(pkg3.ConfigurationHash);
            Assert.NotNull(pkg3.Signature);

            // Verify v3's content matches v1's content
            using var doc1 = System.Text.Json.JsonDocument.Parse(pkg1.Content);
            using var doc3 = System.Text.Json.JsonDocument.Parse(pkg3.Content);
            Assert.Equal(5000, doc3.RootElement.GetProperty("server").GetProperty("port").GetInt32());

            // 3. Source v1 is unmutated and intact
            var sourcePkg1 = await pkgRepo.GetByVersionNumberAsync("default", 1);
            Assert.NotNull(sourcePkg1);
            Assert.True(sourcePkg1.IsActive);
            Assert.Equal(sign1.Hash, sourcePkg1.ConfigurationHash);

            // 4. Failed publication v2 is Superseded
            var failedPub2 = await pubRepo.GetByIdAsync(pub2.Value.Id);
            Assert.NotNull(failedPub2);
            Assert.Equal(ConfigurationLifecycleState.Superseded, failedPub2.Status);
        }

        // -------------------------------------------------------------------
        // 6. Configuration Resolver Integration
        // -------------------------------------------------------------------
        [Fact]
        public async Task ConfigurationResolver_WithPublicationTracking_FiltersOutUnpublishedAndNonActivePackages()
        {
            using var dbContext = CreateInMemoryDbContext();
            var pkgRepo = new ConfigurationPackageRepository(dbContext);
            var targetRepo = new ConfigurationTargetRepository(dbContext);
            var assignRepo = new ConfigurationAssignmentRepository(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var orgRepo = new Repository<Organization>(dbContext);
            var siteRepo = new Repository<Site>(dbContext);
            var pubRepo = new ConfigurationPublicationRepository(dbContext);

            var validator = new ConfigurationValidatorService();
            var normalizer = new ConfigurationNormalizer(validator);
            var deltaEngine = new ConfigurationDeltaEngine(normalizer, validator);
            var signingService = CreateSigningService(dbContext);

            var org = new Organization { Name = "Org Integ Resolver", Code = "ORGINTEG2", Status = "Active" };
            await orgRepo.AddAsync(org);

            var ws = new Workstation { PcId = "PC-PUB-01", Hostname = "pc-pub-01", IpAddress = "10.0.0.60", MacAddress = "00:11:22:33:44:66", OrganizationEntityId = org.Id };
            await wsRepo.AddAsync(ws);

            var globalTarget = ConfigurationTarget.CreateGlobal(org.Id);
            await targetRepo.AddAsync(globalTarget);

            // Package 1: Created & Signed but NOT Active Publication
            var pkg1 = ConfigurationPackage.CreateFull("default", 1, GetValidConfigPayload(5000));
            var sign1 = await signingService.SignPackageAsync(pkg1.Content);
            pkg1.SetCryptographicSignature(sign1.Hash, sign1.Signature, sign1.Algorithm, sign1.KeyId);
            await pkgRepo.AddAsync(pkg1);

            var assign1 = ConfigurationAssignment.Create(pkg1.Id, globalTarget.Id);
            await assignRepo.AddAsync(assign1);

            await dbContext.SaveChangesAsync();

            var resolver = new ConfigurationResolver(
                wsRepo, orgRepo, siteRepo, groupRepo, assignRepo, targetRepo, pkgRepo,
                deltaEngine, normalizer, validator, pubRepo);

            // Resolve effective configuration before activating publication -> Should fall back to default
            var resUnpublished = await resolver.ResolveEffectiveConfigurationAsync(ws.Id);
            Assert.True(resUnpublished.IsSuccess);
            Assert.Empty(resUnpublished.Value!.AppliedSources); // No published active packages selected

            // Now publish and activate publication
            var publishHandler = new PublishConfigurationCommandHandler(pkgRepo, targetRepo, assignRepo, pubRepo, signingService, validator, dbContext);
            var activateHandler = new ActivateConfigurationCommandHandler(pubRepo, pkgRepo, signingService, dbContext);

            var pubRes = await publishHandler.HandleAsync(new PublishConfigurationCommand(pkg1.Id, globalTarget.Id));
            await activateHandler.HandleAsync(new ActivateConfigurationCommand(pubRes.Value!.Id));

            // Resolve again -> Should select package 1 now that it is Active!
            var resPublished = await resolver.ResolveEffectiveConfigurationAsync(ws.Id);
            Assert.True(resPublished.IsSuccess);
            Assert.Single(resPublished.Value!.AppliedSources);
            Assert.Equal(pkg1.Id, resPublished.Value.AppliedSources[0].PackageId);
        }

        // -------------------------------------------------------------------
        // 7. Idempotency Tests
        // -------------------------------------------------------------------
        [Fact]
        public async Task PublishConfiguration_WithSameIdempotencyKey_IsIdempotent()
        {
            using var dbContext = CreateInMemoryDbContext();
            var pkgRepo = new ConfigurationPackageRepository(dbContext);
            var targetRepo = new ConfigurationTargetRepository(dbContext);
            var assignRepo = new ConfigurationAssignmentRepository(dbContext);
            var pubRepo = new ConfigurationPublicationRepository(dbContext);
            var signingService = CreateSigningService(dbContext);
            var validator = new ConfigurationValidatorService();

            var orgId = Guid.NewGuid();
            var target = ConfigurationTarget.CreateGlobal(orgId);
            await targetRepo.AddAsync(target);

            var pkg = ConfigurationPackage.CreateFull("default", 1, GetValidConfigPayload());
            var sign = await signingService.SignPackageAsync(pkg.Content);
            pkg.SetCryptographicSignature(sign.Hash, sign.Signature, sign.Algorithm, sign.KeyId);
            await pkgRepo.AddAsync(pkg);
            await dbContext.SaveChangesAsync();

            var handler = new PublishConfigurationCommandHandler(pkgRepo, targetRepo, assignRepo, pubRepo, signingService, validator, dbContext);

            string idempotencyKey = "KEY-PUBLISH-1001";

            var res1 = await handler.HandleAsync(new PublishConfigurationCommand(pkg.Id, target.Id, IdempotencyKey: idempotencyKey));
            Assert.True(res1.IsSuccess);

            var res2 = await handler.HandleAsync(new PublishConfigurationCommand(pkg.Id, target.Id, IdempotencyKey: idempotencyKey));
            Assert.True(res2.IsSuccess);

            Assert.Equal(res1.Value!.Id, res2.Value!.Id);
        }
    }
}

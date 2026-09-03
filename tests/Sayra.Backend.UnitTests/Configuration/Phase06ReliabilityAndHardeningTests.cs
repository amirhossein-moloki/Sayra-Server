using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sayra.Backend.Api.Controllers;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Application.Configuration.Models;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Infrastructure.Security;
using Xunit;

namespace Sayra.Backend.UnitTests.Configuration
{
    public class Phase06ReliabilityAndHardeningTests
    {
        private static string GetConfigJson(int port) => $@"{{
          ""version"": ""1.0"",
          ""server"": {{ ""ipAddress"": ""192.168.1.1"", ""port"": {port} }},
          ""discovery"": {{ ""enabled"": true, ""port"": 37020 }},
          ""heartbeat"": {{ ""intervalSeconds"": 10, ""timeoutSeconds"": 30 }},
          ""kiosk"": {{ ""enabled"": true, ""allowShellEscape"": false, ""autoLoginGamer"": false, ""idleTimeoutMinutes"": 15 }},
          ""localization"": {{ ""culture"": ""en-US"", ""timeZone"": ""UTC"" }},
          ""security"": {{ ""enableSsl"": true, ""requireEncryption"": true, ""maxFailedAttempts"": 5 }}
        }}";

        private static ApplicationDbContext CreateDbContext(string? dbName = null)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: dbName ?? $"Reliability_{Guid.NewGuid():N}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            return new ApplicationDbContext(options);
        }

        public class FailingRedisCache : IConfigurationCache
        {
            public Task<CachedEffectiveConfiguration?> GetEffectiveConfigurationAsync(Guid organizationId, Guid workstationId, Guid? siteId, List<Guid> groupIds, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("Redis cluster connection refused!");

            public Task SetEffectiveConfigurationAsync(Guid organizationId, Guid workstationId, Guid? siteId, List<Guid> groupIds, CachedEffectiveConfiguration cachedConfig, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task InvalidateWorkstationAsync(Guid organizationId, Guid workstationId, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task InvalidateScopeAsync(Guid organizationId, ConfigurationTargetType targetType, Guid? targetId, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task InvalidateTargetAsync(Guid organizationId, Guid targetId, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task<CachedPublicationMetadata?> GetPublicationMetadataAsync(Guid organizationId, Guid targetId, CancellationToken cancellationToken = default)
                => Task.FromResult<CachedPublicationMetadata?>(null);

            public Task SetPublicationMetadataAsync(Guid organizationId, Guid targetId, CachedPublicationMetadata metadata, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task InvalidatePublicationMetadataAsync(Guid organizationId, Guid targetId, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task<IDisposable?> AcquireStampedeLockAsync(Guid organizationId, Guid workstationId, CancellationToken cancellationToken = default)
                => Task.FromResult<IDisposable?>(null);
        }

        [Fact]
        public async Task Redis_Unavailable_Resolver_Falls_Back_To_PostgreSQL_Without_Failure()
        {
            using var dbContext = CreateDbContext();

            var pkgRepo = new ConfigurationPackageRepository(dbContext);
            var targetRepo = new ConfigurationTargetRepository(dbContext);
            var assignRepo = new ConfigurationAssignmentRepository(dbContext);
            var pubRepo = new ConfigurationPublicationRepository(dbContext);
            var keyRegistryRepo = new ConfigurationKeyRegistryRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var orgRepo = new Repository<Organization>(dbContext);
            var siteRepo = new Repository<Site>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);

            var validator = new ConfigurationValidatorService();
            var normalizer = new ConfigurationNormalizer(validator);
            var deltaEngine = new ConfigurationDeltaEngine(normalizer, validator);
            var canonicalSerializer = new CanonicalConfigurationSerializer();
            var hashService = new ConfigurationHashService();
            var cryptoService = new CryptographicService();
            var privateKeyProvider = new SigningPrivateKeyProvider(Options.Create(new SecurityOptions()));
            var keyRegistry = new ConfigurationKeyRegistry(keyRegistryRepo, privateKeyProvider, dbContext);
            var signingService = new ConfigurationSigningService(canonicalSerializer, hashService, keyRegistry, privateKeyProvider, cryptoService);

            var (keyId, pubPem, privPem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-rel-01");
            privateKeyProvider.RegisterTestKeyPair("key-rel-01", pubPem, privPem);
            await keyRegistryRepo.AddAsync(ConfigurationSigningKey.Create("key-rel-01", pubPem, "RSA-SHA256", SigningKeyStatus.Active));

            var org = new Organization { Name = "Org Rel", Code = "ORGREL", Status = "Active" };
            await orgRepo.AddAsync(org);
            var ws = new Workstation { PcId = "PC-REL-01", Hostname = "pc-rel-01", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:05", OrganizationEntityId = org.Id };
            await wsRepo.AddAsync(ws);
            await dbContext.SaveChangesAsync();

            var target = ConfigurationTarget.CreateGlobal(org.Id);
            await targetRepo.AddAsync(target);

            var pkg = ConfigurationPackage.CreateFull("default", 1, GetConfigJson(5000));
            var sign = await signingService.SignPackageAsync(pkg.Content);
            pkg.SetCryptographicSignature(sign.Hash, sign.Signature, sign.Algorithm, sign.KeyId);
            await pkgRepo.AddAsync(pkg);

            var pub = ConfigurationPublication.Create(pkg.Id, 1, "v1", target.Id, org.Id, sign.Hash, sign.Signature, sign.KeyId, "RSA-SHA256", "admin");
            pub.Publish("admin");
            pub.Activate("admin");
            await pubRepo.AddAsync(pub);
            await assignRepo.AddAsync(ConfigurationAssignment.Create(pkg.Id, target.Id));
            await dbContext.SaveChangesAsync();

            // Resolver with Failing Redis Cache
            var resolver = new ConfigurationResolver(
                wsRepo, orgRepo, siteRepo, groupRepo, assignRepo, targetRepo, pkgRepo,
                deltaEngine, normalizer, validator, pubRepo, new FailingRedisCache());

            var res = await resolver.ResolveEffectiveConfigurationAsync(ws.Id);
            Assert.True(res.IsSuccess);
            Assert.Contains(@"""port"":5000", res.Value!.EffectiveConfigurationJson);
        }

        [Fact]
        public async Task Cryptographic_Tampering_Payload_Hash_And_Signature_Modifications_Fails_Verification()
        {
            using var dbContext = CreateDbContext();
            var canonicalSerializer = new CanonicalConfigurationSerializer();
            var hashService = new ConfigurationHashService();
            var cryptoService = new CryptographicService();
            var privateKeyProvider = new SigningPrivateKeyProvider(Options.Create(new SecurityOptions()));

            var (keyId, pubPem, privPem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-tamp-01");
            privateKeyProvider.RegisterTestKeyPair("key-tamp-01", pubPem, privPem);

            var keyRegistryRepoMock = new ConfigurationKeyRegistryRepository(dbContext);
            await keyRegistryRepoMock.AddAsync(ConfigurationSigningKey.Create("key-tamp-01", pubPem, "RSA-SHA256", SigningKeyStatus.Active));
            await keyRegistryRepoMock.AddAsync(ConfigurationSigningKey.Create("key-revoked-01", pubPem, "RSA-SHA256", SigningKeyStatus.Revoked));
            await dbContext.SaveChangesAsync();

            var keyRegistry = new ConfigurationKeyRegistry(keyRegistryRepoMock, privateKeyProvider, dbContext);
            var signingService = new ConfigurationSigningService(canonicalSerializer, hashService, keyRegistry, privateKeyProvider, cryptoService);

            string validPayload = GetConfigJson(5000);
            var sign = await signingService.SignPackageAsync(validPayload, "key-tamp-01");

            // 1. Valid Check
            var verifyValid = await signingService.VerifyPackageAsync(validPayload, sign.Hash, sign.Signature, "key-tamp-01");
            Assert.True(verifyValid.IsValid);

            // 2. Tampered Payload
            string tamperedPayload = GetConfigJson(9999);
            var verifyTamperedPayload = await signingService.VerifyPackageAsync(tamperedPayload, sign.Hash, sign.Signature, "key-tamp-01");
            Assert.False(verifyTamperedPayload.IsValid);

            // 3. Tampered Hash
            string tamperedHash = "a" + sign.Hash.Substring(1);
            var verifyTamperedHash = await signingService.VerifyPackageAsync(validPayload, tamperedHash, sign.Signature, "key-tamp-01");
            Assert.False(verifyTamperedHash.IsValid);

            // 4. Tampered Signature
            string tamperedSignature = "AAAA" + sign.Signature.Substring(4);
            var verifyTamperedSig = await signingService.VerifyPackageAsync(validPayload, sign.Hash, tamperedSignature, "key-tamp-01");
            Assert.False(verifyTamperedSig.IsValid);

            // 5. Revoked Key
            var verifyRevokedKey = await signingService.VerifyPackageAsync(validPayload, sign.Hash, sign.Signature, "key-revoked-01");
            Assert.False(verifyRevokedKey.IsValid);
            Assert.Contains("revoked", verifyRevokedKey.FailureReason);

            // 6. Unknown Key
            var verifyUnknownKey = await signingService.VerifyPackageAsync(validPayload, sign.Hash, sign.Signature, "unknown-key-99");
            Assert.False(verifyUnknownKey.IsValid);
        }

        [Fact]
        public async Task RateLimiting_Rapid_Repeated_Sync_Returns_429_TooManyRequests()
        {
            using var dbContext = CreateDbContext();

            var pkgRepo = new ConfigurationPackageRepository(dbContext);
            var targetRepo = new ConfigurationTargetRepository(dbContext);
            var assignRepo = new ConfigurationAssignmentRepository(dbContext);
            var pubRepo = new ConfigurationPublicationRepository(dbContext);
            var keyRegistryRepo = new ConfigurationKeyRegistryRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var orgRepo = new Repository<Organization>(dbContext);
            var siteRepo = new Repository<Site>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);

            var validator = new ConfigurationValidatorService();
            var normalizer = new ConfigurationNormalizer(validator);
            var deltaEngine = new ConfigurationDeltaEngine(normalizer, validator);
            var canonicalSerializer = new CanonicalConfigurationSerializer();
            var hashService = new ConfigurationHashService();
            var cryptoService = new CryptographicService();
            var privateKeyProvider = new SigningPrivateKeyProvider(Options.Create(new SecurityOptions()));
            var keyRegistry = new ConfigurationKeyRegistry(keyRegistryRepo, privateKeyProvider, dbContext);
            var signingService = new ConfigurationSigningService(canonicalSerializer, hashService, keyRegistry, privateKeyProvider, cryptoService);

            var (keyId, pubPem, privPem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-rate-01");
            privateKeyProvider.RegisterTestKeyPair("key-rate-01", pubPem, privPem);
            await keyRegistryRepo.AddAsync(ConfigurationSigningKey.Create("key-rate-01", pubPem, "RSA-SHA256", SigningKeyStatus.Active));

            var org = new Organization { Name = "Org Rate", Code = "ORGRATE", Status = "Active" };
            await orgRepo.AddAsync(org);
            var ws = new Workstation { PcId = "PC-RATE-01", Hostname = "pc-rate-01", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:06", OrganizationEntityId = org.Id };
            await wsRepo.AddAsync(ws);
            await dbContext.SaveChangesAsync();

            var target = ConfigurationTarget.CreateGlobal(org.Id);
            await targetRepo.AddAsync(target);

            var pkg = ConfigurationPackage.CreateFull("default", 1, GetConfigJson(5000));
            var sign = await signingService.SignPackageAsync(pkg.Content);
            pkg.SetCryptographicSignature(sign.Hash, sign.Signature, sign.Algorithm, sign.KeyId);
            await pkgRepo.AddAsync(pkg);

            var pub = ConfigurationPublication.Create(pkg.Id, 1, "v1", target.Id, org.Id, sign.Hash, sign.Signature, sign.KeyId, "RSA-SHA256", "admin");
            pub.Publish("admin");
            pub.Activate("admin");
            await pubRepo.AddAsync(pub);
            await assignRepo.AddAsync(ConfigurationAssignment.Create(pkg.Id, target.Id));
            await dbContext.SaveChangesAsync();

            var resolver = new ConfigurationResolver(wsRepo, orgRepo, siteRepo, groupRepo, assignRepo, targetRepo, pkgRepo, deltaEngine, normalizer, validator, pubRepo);
            var syncHandler = new SynchronizeConfigurationQueryHandler(wsRepo, resolver, signingService, hashService, canonicalSerializer, pkgRepo, deltaEngine, NullLogger<SynchronizeConfigurationQueryHandler>.Instance);

            var controller = new ConfigurationSyncController(syncHandler);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
            controller.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.100.50");
            controller.HttpContext.Items["UserPrincipal"] = new UserPrincipal { IsAuthenticated = true, PcId = "PC-RATE-01", OrganizationId = org.Id };

            ConfigurationSyncController.ResetRateLimitMap();

            // First request -> 200 OK
            var res1 = await controller.SynchronizeConfigurationAsync(null, CancellationToken.None);
            Assert.IsType<OkObjectResult>(res1);

            // Immediate second request from same IP -> 429 Too Many Requests
            var res2 = await controller.SynchronizeConfigurationAsync(null, CancellationToken.None);
            var objectResult = Assert.IsType<ObjectResult>(res2);
            Assert.Equal(429, objectResult.StatusCode);
        }

        [Fact]
        public async Task Concurrency_Parallel_Sync_Requests_Do_Not_Cause_Data_Races_Or_Corrupt_State()
        {
            string dbName = $"Conc_DB_{Guid.NewGuid():N}";
            using (var seedDb = CreateDbContext(dbName))
            {
                var pkgRepo = new ConfigurationPackageRepository(seedDb);
                var targetRepo = new ConfigurationTargetRepository(seedDb);
                var assignRepo = new ConfigurationAssignmentRepository(seedDb);
                var pubRepo = new ConfigurationPublicationRepository(seedDb);
                var keyRegistryRepo = new ConfigurationKeyRegistryRepository(seedDb);
                var wsRepo = new Repository<Workstation>(seedDb);
                var orgRepo = new Repository<Organization>(seedDb);

                var validator = new ConfigurationValidatorService();
                var normalizer = new ConfigurationNormalizer(validator);
                var canonicalSerializer = new CanonicalConfigurationSerializer();
                var hashService = new ConfigurationHashService();
                var cryptoService = new CryptographicService();
                var privateKeyProvider = new SigningPrivateKeyProvider(Options.Create(new SecurityOptions()));
                var keyRegistry = new ConfigurationKeyRegistry(keyRegistryRepo, privateKeyProvider, seedDb);
                var signingService = new ConfigurationSigningService(canonicalSerializer, hashService, keyRegistry, privateKeyProvider, cryptoService);

                var (keyId, pubPem, privPem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-conc-01");
                privateKeyProvider.RegisterTestKeyPair("key-conc-01", pubPem, privPem);
                await keyRegistryRepo.AddAsync(ConfigurationSigningKey.Create("key-conc-01", pubPem, "RSA-SHA256", SigningKeyStatus.Active));

                var org = new Organization { Name = "Org Conc", Code = "ORGCONC", Status = "Active" };
                await orgRepo.AddAsync(org);
                var ws = new Workstation { PcId = "PC-CONC-01", Hostname = "pc-conc-01", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:07", OrganizationEntityId = org.Id };
                await wsRepo.AddAsync(ws);
                await seedDb.SaveChangesAsync();

                var target = ConfigurationTarget.CreateGlobal(org.Id);
                await targetRepo.AddAsync(target);

                var pkg = ConfigurationPackage.CreateFull("default", 1, GetConfigJson(5000));
                var sign = await signingService.SignPackageAsync(pkg.Content);
                pkg.SetCryptographicSignature(sign.Hash, sign.Signature, sign.Algorithm, sign.KeyId);
                await pkgRepo.AddAsync(pkg);

                var pub = ConfigurationPublication.Create(pkg.Id, 1, "v1", target.Id, org.Id, sign.Hash, sign.Signature, sign.KeyId, "RSA-SHA256", "admin");
                pub.Publish("admin");
                pub.Activate("admin");
                await pubRepo.AddAsync(pub);
                await assignRepo.AddAsync(ConfigurationAssignment.Create(pkg.Id, target.Id));
                await seedDb.SaveChangesAsync();
            }

            // Execute 20 concurrent sync requests, each with its own scoped DbContext instance
            Guid orgId;
            Guid wsId;
            string pcId = "PC-CONC-01";
            using (var checkDb = CreateDbContext(dbName))
            {
                var ws = await checkDb.Workstations.FirstAsync(w => w.PcId == pcId);
                wsId = ws.Id;
                orgId = ws.OrganizationEntityId!.Value;
            }

            var tasks = new List<Task<Shared.Result<ConfigurationSyncResult>>>();
            for (int i = 0; i < 20; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    using var taskDb = CreateDbContext(dbName);
                    var pkgRepo = new ConfigurationPackageRepository(taskDb);
                    var targetRepo = new ConfigurationTargetRepository(taskDb);
                    var assignRepo = new ConfigurationAssignmentRepository(taskDb);
                    var pubRepo = new ConfigurationPublicationRepository(taskDb);
                    var keyRegistryRepo = new ConfigurationKeyRegistryRepository(taskDb);
                    var wsRepo = new Repository<Workstation>(taskDb);
                    var orgRepo = new Repository<Organization>(taskDb);
                    var siteRepo = new Repository<Site>(taskDb);
                    var groupRepo = new WorkstationGroupRepository(taskDb);

                    var validator = new ConfigurationValidatorService();
                    var normalizer = new ConfigurationNormalizer(validator);
                    var deltaEngine = new ConfigurationDeltaEngine(normalizer, validator);
                    var canonicalSerializer = new CanonicalConfigurationSerializer();
                    var hashService = new ConfigurationHashService();
                    var cryptoService = new CryptographicService();
                    var privateKeyProvider = new SigningPrivateKeyProvider(Options.Create(new SecurityOptions()));
                    var keyRegistry = new ConfigurationKeyRegistry(keyRegistryRepo, privateKeyProvider, taskDb);
                    var signingService = new ConfigurationSigningService(canonicalSerializer, hashService, keyRegistry, privateKeyProvider, cryptoService);

                    var resolver = new ConfigurationResolver(wsRepo, orgRepo, siteRepo, groupRepo, assignRepo, targetRepo, pkgRepo, deltaEngine, normalizer, validator, pubRepo);
                    var syncHandler = new SynchronizeConfigurationQueryHandler(wsRepo, resolver, signingService, hashService, canonicalSerializer, pkgRepo, deltaEngine, NullLogger<SynchronizeConfigurationQueryHandler>.Instance);

                    return await syncHandler.HandleAsync(new SynchronizeConfigurationQuery(pcId, null, wsId, orgId));
                }));
            }

            var results = await Task.WhenAll(tasks);
            Assert.Equal(20, results.Length);
            foreach (var r in results)
            {
                Assert.True(r.IsSuccess);
                Assert.Equal("v1", r.Value!.Version);
            }
        }

        [Fact]
        public async Task Performance_Resolution_And_Sync_Query_Latency_Benchmark()
        {
            using var dbContext = CreateDbContext();

            var pkgRepo = new ConfigurationPackageRepository(dbContext);
            var targetRepo = new ConfigurationTargetRepository(dbContext);
            var assignRepo = new ConfigurationAssignmentRepository(dbContext);
            var pubRepo = new ConfigurationPublicationRepository(dbContext);
            var keyRegistryRepo = new ConfigurationKeyRegistryRepository(dbContext);
            var wsRepo = new Repository<Workstation>(dbContext);
            var orgRepo = new Repository<Organization>(dbContext);
            var siteRepo = new Repository<Site>(dbContext);
            var groupRepo = new WorkstationGroupRepository(dbContext);

            var validator = new ConfigurationValidatorService();
            var normalizer = new ConfigurationNormalizer(validator);
            var deltaEngine = new ConfigurationDeltaEngine(normalizer, validator);
            var canonicalSerializer = new CanonicalConfigurationSerializer();
            var hashService = new ConfigurationHashService();
            var cryptoService = new CryptographicService();
            var privateKeyProvider = new SigningPrivateKeyProvider(Options.Create(new SecurityOptions()));
            var keyRegistry = new ConfigurationKeyRegistry(keyRegistryRepo, privateKeyProvider, dbContext);
            var signingService = new ConfigurationSigningService(canonicalSerializer, hashService, keyRegistry, privateKeyProvider, cryptoService);

            var (keyId, pubPem, privPem) = SigningPrivateKeyProvider.GetOrCreateEphemeralKeyPair("key-perf-01");
            privateKeyProvider.RegisterTestKeyPair("key-perf-01", pubPem, privPem);
            await keyRegistryRepo.AddAsync(ConfigurationSigningKey.Create("key-perf-01", pubPem, "RSA-SHA256", SigningKeyStatus.Active));

            var org = new Organization { Name = "Org Perf", Code = "ORGPERF", Status = "Active" };
            await orgRepo.AddAsync(org);
            var ws = new Workstation { PcId = "PC-PERF-01", Hostname = "pc-perf-01", IpAddress = "10.0.0.1", MacAddress = "00:11:22:33:44:08", OrganizationEntityId = org.Id };
            await wsRepo.AddAsync(ws);
            await dbContext.SaveChangesAsync();

            var target = ConfigurationTarget.CreateGlobal(org.Id);
            await targetRepo.AddAsync(target);

            var pkg = ConfigurationPackage.CreateFull("default", 1, GetConfigJson(5000));
            var sign = await signingService.SignPackageAsync(pkg.Content);
            pkg.SetCryptographicSignature(sign.Hash, sign.Signature, sign.Algorithm, sign.KeyId);
            await pkgRepo.AddAsync(pkg);

            var pub = ConfigurationPublication.Create(pkg.Id, 1, "v1", target.Id, org.Id, sign.Hash, sign.Signature, sign.KeyId, "RSA-SHA256", "admin");
            pub.Publish("admin");
            pub.Activate("admin");
            await pubRepo.AddAsync(pub);
            await assignRepo.AddAsync(ConfigurationAssignment.Create(pkg.Id, target.Id));
            await dbContext.SaveChangesAsync();

            var resolver = new ConfigurationResolver(wsRepo, orgRepo, siteRepo, groupRepo, assignRepo, targetRepo, pkgRepo, deltaEngine, normalizer, validator, pubRepo);
            var syncHandler = new SynchronizeConfigurationQueryHandler(wsRepo, resolver, signingService, hashService, canonicalSerializer, pkgRepo, deltaEngine, NullLogger<SynchronizeConfigurationQueryHandler>.Instance);

            // Measure 50 sequential sync executions
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 50; i++)
            {
                var r = await syncHandler.HandleAsync(new SynchronizeConfigurationQuery(ws.PcId, null, ws.Id, org.Id));
                Assert.True(r.IsSuccess);
            }
            sw.Stop();

            double avgMs = sw.Elapsed.TotalMilliseconds / 50.0;
            // Average resolution + sync + RSA signing should be under 50ms per iteration in-memory
            Assert.True(avgMs < 50.0, $"Average sync latency {avgMs:F2}ms exceeded threshold of 50ms.");
        }
    }
}

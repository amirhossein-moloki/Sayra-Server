using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Persistence;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class UpdatePersistenceTests
    {
        private ApplicationDbContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: $"UpdateTestDb_{Guid.NewGuid():N}")
                .Options;

            return new ApplicationDbContext(options);
        }

        [Fact]
        public async Task Repository_AddAndRetrieveUpdateReleaseWithPackages_Succeeds()
        {
            using var context = CreateInMemoryDbContext();
            var releaseRepo = new UpdateReleaseRepository(context);
            var packageRepo = new UpdatePackageRepository(context);

            var orgId = Guid.NewGuid();
            var release = UpdateRelease.Create(orgId, "v1.5.0", UpdateReleaseType.Standard, "Notes", "admin");
            var package = UpdatePackage.Create(release.Id, "release-v1.5.0.spk", 2048576, "storage/v1.5.0/release.spk", UpdatePackageType.Spk);

            release.AddPackage(package);

            await releaseRepo.AddAsync(release);
            await packageRepo.AddAsync(package);
            await context.SaveChangesAsync();

            var fetchedRelease = await releaseRepo.GetByOrganizationAndVersionAsync(orgId, "v1.5.0");

            Assert.NotNull(fetchedRelease);
            Assert.Equal(orgId, fetchedRelease!.OrganizationId);
            Assert.Equal("v1.5.0", fetchedRelease.Version);
            Assert.Single(fetchedRelease.Packages);

            var fetchedPackage = await packageRepo.GetByStorageKeyAsync("storage/v1.5.0/release.spk");
            Assert.NotNull(fetchedPackage);
            Assert.Equal(release.Id, fetchedPackage!.ReleaseId);
            Assert.Equal(2048576, fetchedPackage.Size);
        }

        [Fact]
        public async Task Repository_GetActiveRelease_ReturnsActiveRelease()
        {
            using var context = CreateInMemoryDbContext();
            var releaseRepo = new UpdateReleaseRepository(context);

            var orgId = Guid.NewGuid();
            var release1 = UpdateRelease.Create(orgId, "v1.0.0");
            release1.TransitionTo(UpdateReleaseStatus.Validated);
            release1.TransitionTo(UpdateReleaseStatus.Ready);
            release1.TransitionTo(UpdateReleaseStatus.Published);
            release1.TransitionTo(UpdateReleaseStatus.Active);

            var release2 = UpdateRelease.Create(orgId, "v1.1.0");

            await releaseRepo.AddAsync(release1);
            await releaseRepo.AddAsync(release2);
            await context.SaveChangesAsync();

            var activeRelease = await releaseRepo.GetActiveReleaseAsync(orgId);

            Assert.NotNull(activeRelease);
            Assert.Equal("v1.0.0", activeRelease!.Version);
            Assert.Equal(UpdateReleaseStatus.Active, activeRelease.Status);
        }
    }
}

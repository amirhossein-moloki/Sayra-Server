using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sayra.Backend.Api;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Persistence;

namespace Sayra.Backend.IntegrationTests
{
    public static class TestAdminSeeder
    {
        public static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        public static async Task EnsureAdminUserCreatedAsync(WebApplicationFactory<Program> factory)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();

            var existingUser = await db.Users.FindAsync(AdminUserId);
            if (existingUser == null)
            {
                var adminUser = new User
                {
                    UserId = "USR-ADMIN-001",
                    Username = "testadmin",
                    DisplayName = "Test Administrator",
                    Email = "admin@test.dev",
                    Role = UserRole.Administrator,
                    Status = UserAccountState.Active
                };

                typeof(BaseEntity).GetProperty("Id")?.SetValue(adminUser, AdminUserId);

                db.Users.Add(adminUser);
                await db.SaveChangesAsync();
            }
        }
    }
}

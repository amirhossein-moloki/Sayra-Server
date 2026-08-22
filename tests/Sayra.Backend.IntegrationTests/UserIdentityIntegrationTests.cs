using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sayra.Backend.Api;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Persistence;
using Xunit;

namespace Sayra.Backend.IntegrationTests
{
    public class UserIdentityIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public UserIdentityIntegrationTests(WebApplicationFactory<Program> factory)
        {
            EnvLoader.Load();
            _factory = factory;
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task User_Persistence_And_State_Lifecycle_In_PostgreSQL()
        {
            string uniqueKey = Guid.NewGuid().ToString("N").Substring(0, 8);
            string username = $"user_{uniqueKey}";
            Guid userId;

            using (var scope = _factory.Services.CreateScope())
            {
                var userRepo = scope.ServiceProvider.GetRequiredService<IRepository<User>>();
                var credentialRepo = scope.ServiceProvider.GetRequiredService<IRepository<UserCredential>>();
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                var user = new User
                {
                    Username = username,
                    Email = $"{username}@example.com",
                    DisplayName = "Operator One",
                    Role = UserRole.Operator,
                    Status = UserAccountState.Pending
                };
                user.NormalizeAndValidate();

                await userRepo.AddAsync(user);

                var (hash, salt, algo, parameters) = hasher.HashPasswordWithDetails("OperatorPass123!");
                var credential = new UserCredential
                {
                    UserEntityId = user.Id,
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    HashAlgorithm = algo,
                    HashParameters = parameters
                };

                await credentialRepo.AddAsync(credential);
                await uow.SaveChangesAsync();
                userId = user.Id;
            }

            // Verify persistence in PostgreSQL using a new scope
            using (var scope = _factory.Services.CreateScope())
            {
                var userRepo = scope.ServiceProvider.GetRequiredService<IRepository<User>>();
                var credentialRepo = scope.ServiceProvider.GetRequiredService<IRepository<UserCredential>>();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                var persistedUser = await userRepo.GetByIdAsync(userId, track: true);
                Assert.NotNull(persistedUser);
                Assert.Equal(username, persistedUser.Username);
                Assert.Equal(UserRole.Operator, persistedUser.Role);
                Assert.Equal(UserAccountState.Pending, persistedUser.Status);
                Assert.StartsWith("USR-", persistedUser.UserId);

                var persistedCred = await credentialRepo.FirstOrDefaultAsync(c => c.UserEntityId == userId, track: false);
                Assert.NotNull(persistedCred);
                Assert.Equal("Argon2id", persistedCred.HashAlgorithm);

                // Test State Machine transition & persistence
                persistedUser.TransitionTo(UserAccountState.Active);
                userRepo.Update(persistedUser);
                await uow.SaveChangesAsync();
            }

            // Confirm updated status in a third scope
            using (var scope = _factory.Services.CreateScope())
            {
                var userRepo = scope.ServiceProvider.GetRequiredService<IRepository<User>>();
                var updatedUser = await userRepo.GetByIdAsync(userId, track: false);
                Assert.NotNull(updatedUser);
                Assert.Equal(UserAccountState.Active, updatedUser.Status);
            }
        }

        [Fact]
        public async Task PostgreSQL_Should_Enforce_Unique_Username_Constraint_On_Users()
        {
            using var scope = _factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            string uniqueKey = Guid.NewGuid().ToString("N").Substring(0, 8);
            string username = $"dupuser_{uniqueKey}";

            var user1 = new User
            {
                Username = username,
                DisplayName = "User One",
                Role = UserRole.Manager,
                Status = UserAccountState.Active
            };
            user1.NormalizeAndValidate();
            dbContext.Users.Add(user1);
            await dbContext.SaveChangesAsync();

            var user2 = new User
            {
                Username = username,
                DisplayName = "User Two",
                Role = UserRole.Administrator,
                Status = UserAccountState.Active
            };
            user2.NormalizeAndValidate();
            dbContext.Users.Add(user2);

            await Assert.ThrowsAsync<DbUpdateException>(async () => await dbContext.SaveChangesAsync());
        }

        [Fact]
        public async Task Legacy_PBKDF2_Credential_Should_Auto_Rehash_To_Argon2id_On_Successful_Login()
        {
            using var scope = _factory.Services.CreateScope();
            var gamerRepo = scope.ServiceProvider.GetRequiredService<IRepository<Gamer>>();
            var gamerCredRepo = scope.ServiceProvider.GetRequiredService<IRepository<GamerCredential>>();
            var userRepo = scope.ServiceProvider.GetRequiredService<IRepository<User>>();
            var userCredRepo = scope.ServiceProvider.GetRequiredService<IRepository<UserCredential>>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            string uniqueKey = Guid.NewGuid().ToString("N").Substring(0, 8);
            string username = $"legacy_{uniqueKey}";
            string email = $"legacy_{uniqueKey}@example.com";
            string password = "LegacyPassword123!";

            // 1. Create gamer with legacy PBKDF2 hash
            byte[] saltBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
            byte[] hashBytes = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
                password,
                saltBytes,
                10000,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                32);
            string pbkdf2Hash = Convert.ToBase64String(hashBytes);
            string pbkdf2Salt = Convert.ToBase64String(saltBytes);

            var gamer = new Gamer
            {
                Username = username,
                Email = email,
                Status = "Active"
            };
            gamer.NormalizeAndValidate();
            await gamerRepo.AddAsync(gamer);

            var user = new User
            {
                Username = username,
                Email = email,
                Role = UserRole.Gamer,
                Status = UserAccountState.Active,
                GamerEntityId = gamer.Id
            };
            user.NormalizeAndValidate();
            await userRepo.AddAsync(user);

            var userCred = new UserCredential
            {
                UserEntityId = user.Id,
                PasswordHash = pbkdf2Hash,
                PasswordSalt = pbkdf2Salt,
                HashAlgorithm = "PBKDF2"
            };
            await userCredRepo.AddAsync(userCred);

            var gamerCred = new GamerCredential
            {
                GamerEntityId = gamer.Id,
                CredentialType = "Password",
                PasswordHash = pbkdf2Hash,
                PasswordSalt = pbkdf2Salt,
                HashAlgorithm = "PBKDF2"
            };
            await gamerCredRepo.AddAsync(gamerCred);

            await uow.SaveChangesAsync();

            // 2. Perform login via API
            var loginRequest = new AuthenticateGamerRequestDto
            {
                UsernameOrEmail = username,
                Password = password
            };

            var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

            var authResult = await loginResponse.Content.ReadFromJsonAsync<AuthenticateGamerResponseDto>();
            Assert.NotNull(authResult);
            Assert.True(authResult.IsSuccess);

            // 3. Verify that the credential hash algorithm was automatically upgraded to Argon2id in DB
            using var scope2 = _factory.Services.CreateScope();
            var userCredRepo2 = scope2.ServiceProvider.GetRequiredService<IRepository<UserCredential>>();
            var rehashedCred = await userCredRepo2.FirstOrDefaultAsync(c => c.UserEntityId == user.Id, track: false);

            Assert.NotNull(rehashedCred);
            Assert.Equal("Argon2id", rehashedCred.HashAlgorithm);
            Assert.NotEqual(pbkdf2Hash, rehashedCred.PasswordHash);
        }

        [Fact]
        public async Task UserCredential_OptimisticConcurrency_Protection()
        {
            Guid userId;
            using (var scope = _factory.Services.CreateScope())
            {
                var userRepo = scope.ServiceProvider.GetRequiredService<IRepository<User>>();
                var credRepo = scope.ServiceProvider.GetRequiredService<IRepository<UserCredential>>();
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                string uniqueKey = Guid.NewGuid().ToString("N").Substring(0, 8);
                var user = new User
                {
                    Username = $"conc_{uniqueKey}",
                    DisplayName = "Concurrent User",
                    Role = UserRole.Operator,
                    Status = UserAccountState.Active
                };
                user.NormalizeAndValidate();
                await userRepo.AddAsync(user);

                var (hash, salt, algo, paramsJson) = hasher.HashPasswordWithDetails("InitialPass123!");
                var cred = new UserCredential
                {
                    UserEntityId = user.Id,
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    HashAlgorithm = algo,
                    HashParameters = paramsJson
                };
                await credRepo.AddAsync(cred);
                await uow.SaveChangesAsync();
                userId = user.Id;
            }

            // Read the same credential in two separate contexts
            using var scope1 = _factory.Services.CreateScope();
            var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cred1 = await db1.UserCredentials.FirstAsync(c => c.UserEntityId == userId);

            using var scope2 = _factory.Services.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cred2 = await db2.UserCredentials.FirstAsync(c => c.UserEntityId == userId);

            // Update first context
            cred1.SetPassword("NewPass111!", "newSalt111", "Argon2id");
            await db1.SaveChangesAsync();

            // Attempt to update second context with stale RowVersion -> Should throw DbUpdateConcurrencyException
            cred2.SetPassword("NewPass222!", "newSalt222", "Argon2id");
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () => await db2.SaveChangesAsync());
        }
    }
}

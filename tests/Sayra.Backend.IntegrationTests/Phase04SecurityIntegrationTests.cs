using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sayra.Backend.Api;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain.Entities;
using Xunit;

namespace Sayra.Backend.IntegrationTests
{
    public class Phase04SecurityIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public Phase04SecurityIntegrationTests(WebApplicationFactory<Program> factory)
        {
            EnvLoader.Load();
            _factory = factory;
            TestAdminSeeder.EnsureAdminUserCreatedAsync(factory).GetAwaiter().GetResult();
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task BruteForce_Lockout_And_Security_Event_Persistence()
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            string username = $"bruteforce_{suffix}";
            string email = $"bruteforce_{suffix}@test.dev";
            string password = "CorrectPassword123!";

            // 1. Create Gamer
            var regResp = await _client.PostAsJsonAsync("/api/gamers", new
            {
                username = username,
                email = email,
                password = password
            });
            Assert.Equal(HttpStatusCode.Created, regResp.StatusCode);

            // 2. Perform 5 consecutive failed logins
            for (int i = 0; i < 5; i++)
            {
                var failResp = await _client.PostAsJsonAsync("/api/auth/login", new AuthenticateGamerRequestDto
                {
                    UsernameOrEmail = username,
                    Password = "WrongPassword999!"
                });
                Assert.True(failResp.StatusCode == HttpStatusCode.Unauthorized || failResp.StatusCode == (HttpStatusCode)423);
            }

            // 3. 6th login attempt (even with correct password) must be rejected with 423 Locked or 401
            var lockedResp = await _client.PostAsJsonAsync("/api/auth/login", new AuthenticateGamerRequestDto
            {
                UsernameOrEmail = username,
                Password = password
            });

            Assert.True(lockedResp.StatusCode == (HttpStatusCode)423 || lockedResp.StatusCode == HttpStatusCode.Unauthorized);

            // 4. Verify SecurityEvents and LoginAttempts persisted in PostgreSQL
            using var scope = _factory.Services.CreateScope();
            var secEventRepo = scope.ServiceProvider.GetRequiredService<IRepository<SecurityEvent>>();
            var loginAttemptRepo = scope.ServiceProvider.GetRequiredService<IRepository<LoginAttempt>>();

            var events = await secEventRepo.FindAsync(e => e.EventType == "ACCOUNT_LOCKED", track: false);
            Assert.NotEmpty(events);

            var attempts = await loginAttemptRepo.FindAsync(l => l.UsernameIdentifier == username.ToLower(), track: false);
            Assert.NotEmpty(attempts);
            Assert.True(attempts[0].AttemptCount >= 5);
        }

        [Fact]
        public async Task Authorization_Denied_Emits_Security_Event()
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            string username = $"gamer_no_admin_{suffix}";

            // Unauthenticated API Call to restricted endpoint
            var getResp = await _client.GetAsync($"/api/sessions/{Guid.NewGuid()}");
            Assert.True(getResp.StatusCode == HttpStatusCode.Unauthorized || getResp.StatusCode == HttpStatusCode.Forbidden);

            // Verify SecurityEvent recorded for AUTHORIZATION_DENIED
            using var scope = _factory.Services.CreateScope();
            var secEventRepo = scope.ServiceProvider.GetRequiredService<IRepository<SecurityEvent>>();

            var deniedEvents = await secEventRepo.FindAsync(e => e.EventType == "AUTHORIZATION_DENIED" || e.EventType == "RESOURCE_ACCESS_DENIED", track: false);
            Assert.NotEmpty(deniedEvents);
        }

        [Fact]
        public async Task Concurrent_Failed_Logins_Correctly_Locks_Account()
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            string username = $"concurrent_lock_{suffix}";

            var regResp = await _client.PostAsJsonAsync("/api/gamers", new
            {
                username = username,
                email = $"{username}@test.dev",
                password = "Password123!"
            });
            Assert.Equal(HttpStatusCode.Created, regResp.StatusCode);

            // Execute 6 parallel failed login attempts
            var tasks = new List<Task<HttpResponseMessage>>();
            for (int i = 0; i < 6; i++)
            {
                tasks.Add(_client.PostAsJsonAsync("/api/auth/login", new AuthenticateGamerRequestDto
                {
                    UsernameOrEmail = username,
                    Password = "WrongPassword!"
                }));
            }

            await Task.WhenAll(tasks);

            // Verify account is locked out
            using var scope = _factory.Services.CreateScope();
            var loginProtection = scope.ServiceProvider.GetRequiredService<ILoginProtectionService>();

            bool isLocked = await loginProtection.IsLockedOutAsync(username);
            Assert.True(isLocked);
        }
    }
}

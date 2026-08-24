using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sayra.Backend.Api;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Infrastructure.Persistence;
using Xunit;

namespace Sayra.Backend.IntegrationTests
{
    public class Phase04FinalVerificationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public Phase04FinalVerificationTests(WebApplicationFactory<Program> factory)
        {
            EnvLoader.Load();
            _factory = factory;
            TestAdminSeeder.EnsureAdminUserCreatedAsync(factory).GetAwaiter().GetResult();
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task Full_Authentication_Session_Lifecycle_Login_Me_And_Logout_Revocation()
        {
            string suffix = Guid.NewGuid().ToString("N")[..8];
            string username = $"sess_user_{suffix}";
            string email = $"{username}@test.dev";
            string password = "StrongPassword123!";

            // 1. Create Gamer
            var createRes = await _client.PostAsJsonAsync("/api/gamers", new
            {
                username = username,
                email = email,
                password = password
            });
            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

            // 2. Login via POST /api/auth/login
            var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new AuthenticateGamerRequestDto
            {
                UsernameOrEmail = username,
                Password = password
            });
            Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
            var authDto = await loginRes.Content.ReadFromJsonAsync<AuthenticateGamerResponseDto>();
            Assert.NotNull(authDto);
            Assert.True(authDto.IsSuccess);
            Assert.False(string.IsNullOrWhiteSpace(authDto.SessionToken));

            // 3. Query GET /api/auth/me with Bearer token -> 200 OK
            var sessionClient = _factory.CreateClient();
            sessionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authDto.SessionToken);

            var meRes = await sessionClient.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.OK, meRes.StatusCode);
            var meContent = await meRes.Content.ReadAsStringAsync();
            Assert.Contains(username, meContent);

            // 4. Logout via POST /api/auth/logout with Bearer token
            var logoutRes = await sessionClient.PostAsync("/api/auth/logout", null);
            Assert.Equal(HttpStatusCode.OK, logoutRes.StatusCode);

            // 5. Subsequent call with revoked token MUST fail closed -> 401 Unauthorized
            var meAfterLogoutRes = await sessionClient.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meAfterLogoutRes.StatusCode);
        }

        [Fact]
        public async Task Password_Change_Revokes_All_Active_Sessions()
        {
            string suffix = Guid.NewGuid().ToString("N")[..8];
            string username = $"pwd_user_{suffix}";
            string email = $"{username}@test.dev";
            string oldPassword = "OldPassword123!";
            string newPassword = "NewPassword456!";

            // 1. Create Gamer
            var createRes = await _client.PostAsJsonAsync("/api/gamers", new
            {
                username = username,
                email = email,
                password = oldPassword
            });
            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
            var gamerDto = await createRes.Content.ReadFromJsonAsync<GamerResponseDto>();
            Assert.NotNull(gamerDto);

            // 2. Login to obtain session token
            var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new AuthenticateGamerRequestDto
            {
                UsernameOrEmail = username,
                Password = oldPassword
            });
            Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
            var authDto = await loginRes.Content.ReadFromJsonAsync<AuthenticateGamerResponseDto>();
            Assert.NotNull(authDto);
            Assert.False(string.IsNullOrWhiteSpace(authDto.SessionToken));

            // 3. Verify session token works on GET /api/auth/me
            var sessionClient = _factory.CreateClient();
            sessionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authDto.SessionToken);

            var meBeforeRes = await sessionClient.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.OK, meBeforeRes.StatusCode);

            // 4. Change Password via POST /api/gamers/{gamerId}/change-password
            var changePwdClient = _factory.CreateClient();
            changePwdClient.DefaultRequestHeaders.Add("X-Gamer-Id", gamerDto.Id.ToString());

            var pwdRes = await changePwdClient.PostAsJsonAsync($"/api/gamers/{gamerDto.Id}/change-password", new ChangeGamerPasswordRequestDto
            {
                CurrentPassword = oldPassword,
                NewPassword = newPassword
            });
            Assert.Equal(HttpStatusCode.OK, pwdRes.StatusCode);

            // 5. Session token created before password change MUST be revoked -> 401 Unauthorized
            var meAfterRes = await sessionClient.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meAfterRes.StatusCode);
        }

        [Fact]
        public async Task Disabled_Account_State_Blocks_Authentication_And_API_Access()
        {
            string suffix = Guid.NewGuid().ToString("N")[..8];
            string username = $"dis_user_{suffix}";
            string email = $"{username}@test.dev";
            string password = "Password123!";

            // 1. Create Gamer
            var createRes = await _client.PostAsJsonAsync("/api/gamers", new
            {
                username = username,
                email = email,
                password = password
            });
            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
            var gamerDto = await createRes.Content.ReadFromJsonAsync<GamerResponseDto>();
            Assert.NotNull(gamerDto);

            // 2. Deactivate Gamer via Admin
            var adminClient = _factory.CreateClient();
            adminClient.DefaultRequestHeaders.Add("X-User-Id", TestAdminSeeder.AdminUserId.ToString());
            adminClient.DefaultRequestHeaders.Add("X-User-Role", "Administrator");

            var deactRes = await adminClient.PostAsync($"/api/gamers/{gamerDto.Id}/deactivate", null);
            Assert.Equal(HttpStatusCode.OK, deactRes.StatusCode);

            // 3. Deactivated Gamer attempting login -> 401 Unauthorized
            var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new AuthenticateGamerRequestDto
            {
                UsernameOrEmail = username,
                Password = password
            });
            Assert.Equal(HttpStatusCode.Unauthorized, loginRes.StatusCode);

            // 4. Deactivated Gamer header call -> 401 Unauthorized
            var gamerClient = _factory.CreateClient();
            gamerClient.DefaultRequestHeaders.Add("X-Gamer-Id", gamerDto.Id.ToString());

            var meRes = await gamerClient.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meRes.StatusCode);
        }

        [Fact]
        public async Task Security_Event_Redaction_And_Audit_Logging_Consistency()
        {
            using var scope = _factory.Services.CreateScope();
            var secEventService = scope.ServiceProvider.GetRequiredService<ISecurityEventService>();
            var secEventRepo = scope.ServiceProvider.GetRequiredService<IRepository<SecurityEvent>>();

            string pcId = $"PC_RED_{Guid.NewGuid():N}"[..12];
            string rawReason = "Failure details. Password=SamplePassword123! Token=sample_token_abc";

            await secEventService.RecordSecurityEventAsync(
                eventType: "LOGIN_FAILED",
                actorId: Guid.NewGuid(),
                actorType: "User",
                deviceId: pcId,
                organizationId: Guid.NewGuid(),
                siteId: Guid.NewGuid(),
                resourceType: "Account",
                resourceId: Guid.NewGuid(),
                action: "LOGIN",
                result: "FAILED",
                failureReason: rawReason);

            var events = await secEventRepo.FindAsync(e => e.DeviceId == pcId, track: false);
            Assert.NotEmpty(events);
            var ev = events[0];

            Assert.NotNull(ev.FailureReason);
            Assert.DoesNotContain("SamplePassword123!", ev.FailureReason);
            Assert.DoesNotContain("sample_token_abc", ev.FailureReason);
            Assert.Contains("Password=[REDACTED]", ev.FailureReason);
            Assert.Contains("Token=[REDACTED]", ev.FailureReason);
        }
    }
}

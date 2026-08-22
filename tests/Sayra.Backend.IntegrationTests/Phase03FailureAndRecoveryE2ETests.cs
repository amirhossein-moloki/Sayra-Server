using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Sayra.Backend.Api;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.IntegrationTests
{
    public class Phase03FailureAndRecoveryE2ETests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public Phase03FailureAndRecoveryE2ETests(WebApplicationFactory<Program> factory)
        {
            EnvLoader.Load();
            TestAdminSeeder.EnsureAdminUserCreatedAsync(factory).GetAwaiter().GetResult();
            _client = factory.CreateClient();
            _client.DefaultRequestHeaders.Add("X-User-Id", TestAdminSeeder.AdminUserId.ToString());
            _client.DefaultRequestHeaders.Add("X-User-Role", "Administrator");
        }

        [Fact]
        public async Task Failure_Scenario_Duplicate_Payment_Key_Conflict_Is_Handled_Safely()
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);

            // Create Gamer
            var createGamerResp = await _client.PostAsJsonAsync("/api/gamers", new
            {
                username = $"payfail_{suffix}",
                email = $"payfail_{suffix}@test.dev",
                password = "Password123!"
            });
            Assert.Equal(HttpStatusCode.Created, createGamerResp.StatusCode);
            var gamer = await createGamerResp.Content.ReadFromJsonAsync<GamerResponseDto>();
            Assert.NotNull(gamer);

            // Deposit 100
            await _client.PostAsJsonAsync($"/api/accounts/{gamer.Id}/deposit", new CreditAccountRequestDto { Amount = 100.00m });

            var accResp = await _client.GetAsync($"/api/gamers/{gamer.Id}/account");
            var acc = await accResp.Content.ReadFromJsonAsync<GamerAccountResponseDto>();
            Assert.NotNull(acc);

            string key = $"FAIL_KEY_{suffix}";

            // 1. Submit Payment 1
            var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/payments")
            {
                Content = JsonContent.Create(new CreatePaymentRequestDto
                {
                    GamerAccountId = acc.Id,
                    Amount = 40.00m,
                    IdempotencyKey = key,
                    Reference = "REF1"
                })
            };
            req1.Headers.Add("Idempotency-Key", key);
            var resp1 = await _client.SendAsync(req1);
            Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);

            // 2. Submit conflicting Payment 2 with same key
            var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/payments")
            {
                Content = JsonContent.Create(new CreatePaymentRequestDto
                {
                    GamerAccountId = acc.Id,
                    Amount = 90.00m, // Different amount -> Conflict
                    IdempotencyKey = key,
                    Reference = "REF2"
                })
            };
            req2.Headers.Add("Idempotency-Key", key);
            var resp2 = await _client.SendAsync(req2);
            Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);

            // Verify final balance is 60.00m (only 40 debited)
            var balResp = await _client.GetAsync($"/api/accounts/{gamer.Id}/balance");
            var bal = await balResp.Content.ReadFromJsonAsync<AccountBalanceResponseDto>();
            Assert.NotNull(bal);
            Assert.Equal(60.00m, bal.Balance);
        }

        [Fact]
        public async Task Failure_Scenario_Insufficient_Balance_Payment_Is_Rejected()
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);

            // Create Gamer with 0 balance
            var createGamerResp = await _client.PostAsJsonAsync("/api/gamers", new
            {
                username = $"payout_{suffix}",
                email = $"payout_{suffix}@test.dev",
                password = "Password123!"
            });
            Assert.Equal(HttpStatusCode.Created, createGamerResp.StatusCode);
            var gamer = await createGamerResp.Content.ReadFromJsonAsync<GamerResponseDto>();
            Assert.NotNull(gamer);

            var accResp = await _client.GetAsync($"/api/gamers/{gamer.Id}/account");
            var acc = await accResp.Content.ReadFromJsonAsync<GamerAccountResponseDto>();
            Assert.NotNull(acc);

            string key = $"POUT_KEY_{suffix}";

            // Try to pay 100 with 0 balance
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/payments")
            {
                Content = JsonContent.Create(new CreatePaymentRequestDto
                {
                    GamerAccountId = acc.Id,
                    Amount = 100.00m,
                    IdempotencyKey = key,
                    Reference = "REF"
                })
            };
            req.Headers.Add("Idempotency-Key", key);

            var resp = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        }

        [Fact]
        public async Task Recovery_Scenario_Client_Reconnect_Syncs_Backend_Authoritative_State()
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);

            // Setup hierarchy
            var orgResp = await _client.PostAsJsonAsync("/api/organizations", new { code = $"ORG_{suffix}", name = $"Org {suffix}" });
            using var orgDoc = JsonDocument.Parse(await orgResp.Content.ReadAsStringAsync());
            Guid orgId = orgDoc.RootElement.GetProperty("id").GetGuid();

            var siteResp = await _client.PostAsJsonAsync("/api/sites", new { organizationId = orgId, code = $"SITE_{suffix}", name = $"Site {suffix}" });
            using var siteDoc = JsonDocument.Parse(await siteResp.Content.ReadAsStringAsync());
            Guid siteId = siteDoc.RootElement.GetProperty("id").GetGuid();

            var zoneResp = await _client.PostAsJsonAsync("/api/zones", new { siteId = siteId, code = $"ZONE_{suffix}", name = $"Zone {suffix}" });
            using var zoneDoc = JsonDocument.Parse(await zoneResp.Content.ReadAsStringAsync());
            Guid zoneId = zoneDoc.RootElement.GetProperty("id").GetGuid();

            string pcId = $"PC_{suffix}";
            string macAddress = string.Format("00:11:22:33:{0:X2}:{1:X2}", Random.Shared.Next(255), Random.Shared.Next(255));
            var regWsResponse = await _client.PostAsJsonAsync("/api/clients", new
            {
                pcId = pcId,
                siteId = $"SITE_{suffix}",
                hostname = $"HOST-{suffix}",
                ipAddress = "192.168.1.100",
                macAddress = macAddress,
                clientVersion = "1.0.0",
                osVersion = "Windows 11"
            });
            using var wsDoc = JsonDocument.Parse(await regWsResponse.Content.ReadAsStringAsync());
            Guid wsId = wsDoc.RootElement.GetProperty("id").GetGuid();

            await _client.PostAsJsonAsync($"/api/workstations/{wsId}/assignment", new { organizationId = orgId, siteId = siteId, zoneId = zoneId });

            var gamerResp = await _client.PostAsJsonAsync("/api/gamers", new
            {
                username = $"rec_{suffix}",
                email = $"rec_{suffix}@test.dev",
                password = "Password123!"
            });
            var gamer = await gamerResp.Content.ReadFromJsonAsync<GamerResponseDto>();
            Assert.NotNull(gamer);

            // 1. Start Session
            var startResp = await _client.PostAsJsonAsync("/api/sessions", new StartSessionRequestDto
            {
                GamerId = gamer.Id,
                WorkstationId = wsId
            });
            Assert.Equal(HttpStatusCode.Created, startResp.StatusCode);
            var session = await startResp.Content.ReadFromJsonAsync<SessionResponseDto>();
            Assert.NotNull(session);

            // Simulate client disconnect and reconnect after time passes
            var reconResp = await _client.GetAsync($"/api/sessions/workstation/{wsId}/active");
            Assert.Equal(HttpStatusCode.OK, reconResp.StatusCode);
            var activeSession = await reconResp.Content.ReadFromJsonAsync<SessionResponseDto>();
            Assert.NotNull(activeSession);
            Assert.Equal(session.SessionId, activeSession.SessionId);
            Assert.Equal("ACTIVE", activeSession.Status);

            // Reconnecting client queries timing
            var timeResp = await _client.GetAsync($"/api/sessions/{session.SessionId}/timing");
            Assert.Equal(HttpStatusCode.OK, timeResp.StatusCode);
            var timing = await timeResp.Content.ReadFromJsonAsync<SessionTimingResponseDto>();
            Assert.NotNull(timing);
            Assert.True(timing.ConsumedDuration >= TimeSpan.Zero);
        }
    }
}

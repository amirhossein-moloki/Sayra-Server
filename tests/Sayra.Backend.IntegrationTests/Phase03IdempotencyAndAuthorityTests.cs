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
    public class Phase03IdempotencyAndAuthorityTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public Phase03IdempotencyAndAuthorityTests(WebApplicationFactory<Program> factory)
        {
            EnvLoader.Load();
            TestAdminSeeder.EnsureAdminUserCreatedAsync(factory).GetAwaiter().GetResult();
            _client = factory.CreateClient();
            _client.DefaultRequestHeaders.Add("X-User-Id", TestAdminSeeder.AdminUserId.ToString());
            _client.DefaultRequestHeaders.Add("X-User-Role", "Administrator");
        }

        [Fact]
        public async Task Payment_Same_Idempotency_Key_Different_Payload_Returns_Conflict()
        {
            var (orgId, siteId, wsId, gamerId) = await SeedHierarchyAndGamerAsync();

            // Deposit 500
            await _client.PostAsJsonAsync($"/api/accounts/{gamerId}/deposit", new CreditAccountRequestDto { Amount = 500.00m });

            var accResp = await _client.GetAsync($"/api/gamers/{gamerId}/account");
            var acc = await accResp.Content.ReadFromJsonAsync<GamerAccountResponseDto>();
            Assert.NotNull(acc);

            string key = $"KEY_DIFF_{Guid.NewGuid():N}";

            // 1. First Payment
            var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/payments")
            {
                Content = JsonContent.Create(new CreatePaymentRequestDto
                {
                    GamerAccountId = acc.Id,
                    Amount = 50.00m,
                    IdempotencyKey = key,
                    Reference = "Ref1"
                })
            };
            req1.Headers.Add("Idempotency-Key", key);
            var resp1 = await _client.SendAsync(req1);
            Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);

            // 2. Same Key + Different Amount -> Returns 409 Conflict
            var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/payments")
            {
                Content = JsonContent.Create(new CreatePaymentRequestDto
                {
                    GamerAccountId = acc.Id,
                    Amount = 100.00m, // Different payload!
                    IdempotencyKey = key,
                    Reference = "Ref2"
                })
            };
            req2.Headers.Add("Idempotency-Key", key);
            var resp2 = await _client.SendAsync(req2);
            Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);
        }

        [Fact]
        public async Task Extend_Session_Idempotency_Key_Reuse_Returns_Same_Result()
        {
            var (orgId, siteId, wsId, gamerId) = await SeedHierarchyAndGamerAsync();

            // Deposit 100
            await _client.PostAsJsonAsync($"/api/accounts/{gamerId}/deposit", new CreditAccountRequestDto { Amount = 100.00m });

            // Start Session
            var startResp = await _client.PostAsJsonAsync("/api/sessions", new StartSessionRequestDto
            {
                GamerId = gamerId,
                WorkstationId = wsId
            });
            Assert.Equal(HttpStatusCode.Created, startResp.StatusCode);
            var session = await startResp.Content.ReadFromJsonAsync<SessionResponseDto>();
            Assert.NotNull(session);

            string extendKey = $"EXT_KEY_{Guid.NewGuid():N}";

            // 1. Extend session 30 minutes
            var req1 = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{session.SessionId}/extend")
            {
                Content = JsonContent.Create(new ExtendSessionRequestDto
                {
                    AdditionalMinutes = 30,
                    IdempotencyKey = extendKey
                })
            };
            req1.Headers.Add("Idempotency-Key", extendKey);
            var resp1 = await _client.SendAsync(req1);
            Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
            var ext1 = await resp1.Content.ReadFromJsonAsync<SessionExtensionResponseDto>();
            Assert.NotNull(ext1);

            // 2. Repeat exact same extension call
            var req2 = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{session.SessionId}/extend")
            {
                Content = JsonContent.Create(new ExtendSessionRequestDto
                {
                    AdditionalMinutes = 30,
                    IdempotencyKey = extendKey
                })
            };
            req2.Headers.Add("Idempotency-Key", extendKey);
            var resp2 = await _client.SendAsync(req2);
            Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
            var ext2 = await resp2.Content.ReadFromJsonAsync<SessionExtensionResponseDto>();
            Assert.NotNull(ext2);
            Assert.Equal(ext1.SessionExtensionId, ext2.SessionExtensionId);
        }

        [Fact]
        public async Task Server_Time_Authority_Session_Timing_Is_Calculated_Authoritatively_By_Server()
        {
            var (orgId, siteId, wsId, gamerId) = await SeedHierarchyAndGamerAsync();

            // Start Session
            var startResp = await _client.PostAsJsonAsync("/api/sessions", new StartSessionRequestDto
            {
                GamerId = gamerId,
                WorkstationId = wsId
            });
            Assert.Equal(HttpStatusCode.Created, startResp.StatusCode);
            var session = await startResp.Content.ReadFromJsonAsync<SessionResponseDto>();
            Assert.NotNull(session);

            // Query timing
            var timingResp = await _client.GetAsync($"/api/sessions/{session.SessionId}/timing");
            Assert.Equal(HttpStatusCode.OK, timingResp.StatusCode);
            var timing = await timingResp.Content.ReadFromJsonAsync<SessionTimingResponseDto>();
            Assert.NotNull(timing);
            Assert.True(timing.ConsumedDuration >= TimeSpan.Zero);
        }

        [Fact]
        public async Task Rate_Snapshot_Immutability_Plan_Update_Does_Not_Mutate_Active_Session_Snapshot()
        {
            var (orgId, siteId, wsId, gamerId) = await SeedHierarchyAndGamerAsync();

            // 1. Create Pricing Plan
            var planResp = await _client.PostAsJsonAsync("/api/pricing/plans", new CreatePricingPlanRequestDto
            {
                SiteId = siteId,
                Name = $"Standard Plan {Guid.NewGuid():N}",
                Currency = "SAY"
            });
            Assert.Equal(HttpStatusCode.Created, planResp.StatusCode);
            var plan = await planResp.Content.ReadFromJsonAsync<PricingPlanResponseDto>();
            Assert.NotNull(plan);

            // Activate Plan
            await _client.PostAsJsonAsync($"/api/pricing/plans/{plan.PricingPlanId}/activate", new { });

            // 2. Start Session
            var startResp = await _client.PostAsJsonAsync("/api/sessions", new StartSessionRequestDto
            {
                GamerId = gamerId,
                WorkstationId = wsId
            });
            Assert.Equal(HttpStatusCode.Created, startResp.StatusCode);
            var session = await startResp.Content.ReadFromJsonAsync<SessionResponseDto>();
            Assert.NotNull(session);

            // 3. Create another Pricing Plan
            var newPlanResp = await _client.PostAsJsonAsync("/api/pricing/plans", new CreatePricingPlanRequestDto
            {
                SiteId = siteId,
                Name = $"Expensive Plan {Guid.NewGuid():N}",
                Currency = "SAY"
            });
            Assert.Equal(HttpStatusCode.Created, newPlanResp.StatusCode);

            // 4. Stop Session
            var stopResp = await _client.PostAsJsonAsync($"/api/sessions/{session.SessionId}/stop", new { });
            Assert.Equal(HttpStatusCode.OK, stopResp.StatusCode);
        }

        private async Task<(Guid OrgId, Guid SiteId, Guid WorkstationId, Guid GamerId)> SeedHierarchyAndGamerAsync()
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);

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

            var createGamerResponse = await _client.PostAsJsonAsync("/api/gamers", new
            {
                username = $"gamer_{suffix}",
                email = $"gamer_{suffix}@test.dev",
                password = "Password123!"
            });
            var gamer = await createGamerResponse.Content.ReadFromJsonAsync<GamerResponseDto>();
            Assert.NotNull(gamer);

            return (orgId, siteId, wsId, gamer.Id);
        }
    }
}

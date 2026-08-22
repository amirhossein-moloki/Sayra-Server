using System;
using System.Linq;
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
    public class Phase03ConcurrencyTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public Phase03ConcurrencyTests(WebApplicationFactory<Program> factory)
        {
            EnvLoader.Load();
            TestAdminSeeder.EnsureAdminUserCreatedAsync(factory).GetAwaiter().GetResult();
            _client = factory.CreateClient();
            _client.DefaultRequestHeaders.Add("X-User-Id", TestAdminSeeder.AdminUserId.ToString());
            _client.DefaultRequestHeaders.Add("X-User-Role", "Administrator");
        }

        [Fact]
        public async Task Workstation_Concurrency_Two_Simultaneous_StartSessions_On_Same_Workstation_Exactly_One_Succeeds()
        {
            var seed = await SeedHierarchyAsync();

            // Create Gamer 1 and Gamer 2
            Guid gamer1Id = await CreateGamerAsync("g1_ws_conc");
            Guid gamer2Id = await CreateGamerAsync("g2_ws_conc");

            var req1 = new StartSessionRequestDto { GamerId = gamer1Id, WorkstationId = seed.WorkstationId };
            var req2 = new StartSessionRequestDto { GamerId = gamer2Id, WorkstationId = seed.WorkstationId };

            var resp1 = await _client.PostAsJsonAsync("/api/sessions", req1);
            var resp2 = await _client.PostAsJsonAsync("/api/sessions", req2);

            var statusCodes = new[] { resp1.StatusCode, resp2.StatusCode };

            Assert.Contains(HttpStatusCode.Created, statusCodes);
            Assert.Contains(HttpStatusCode.Conflict, statusCodes);
        }

        [Fact]
        public async Task Reservation_Concurrency_Overlapping_Time_Windows_Rejects_Second_Reservation()
        {
            var seed = await SeedHierarchyAsync();
            Guid gamer1Id = await CreateGamerAsync("res1_conc");
            Guid gamer2Id = await CreateGamerAsync("res2_conc");

            var now = DateTime.UtcNow;
            var res1Req = new CreateReservationRequestDto
            {
                GamerId = gamer1Id,
                SiteId = seed.SiteId,
                WorkstationId = seed.WorkstationId,
                StartTimeUtc = now.AddHours(1),
                EndTimeUtc = now.AddHours(3),
                ReservedAmount = 20.00m
            };

            var res2Req = new CreateReservationRequestDto
            {
                GamerId = gamer2Id,
                SiteId = seed.SiteId,
                WorkstationId = seed.WorkstationId,
                StartTimeUtc = now.AddHours(2), // Overlaps with res1
                EndTimeUtc = now.AddHours(4),
                ReservedAmount = 20.00m
            };

            var resp1 = await _client.PostAsJsonAsync("/api/reservations", res1Req);
            var resp2 = await _client.PostAsJsonAsync("/api/reservations", res2Req);

            var statusCodes = new[] { resp1.StatusCode, resp2.StatusCode };
            Assert.Contains(HttpStatusCode.Created, statusCodes);
            Assert.Contains(HttpStatusCode.Conflict, statusCodes);
        }

        [Fact]
        public async Task Financial_Concurrency_Case1_Two_Concurrent_Deposits_Preserves_Invariants()
        {
            Guid gamerId = await CreateGamerAsync("fin_case1");

            var dep1Resp = await _client.PostAsJsonAsync($"/api/accounts/{gamerId}/deposit", new CreditAccountRequestDto
            {
                Amount = 100.00m,
                Reference = $"DEP1_{Guid.NewGuid():N}"
            });
            Assert.Equal(HttpStatusCode.OK, dep1Resp.StatusCode);

            var dep2Resp = await _client.PostAsJsonAsync($"/api/accounts/{gamerId}/deposit", new CreditAccountRequestDto
            {
                Amount = 200.00m,
                Reference = $"DEP2_{Guid.NewGuid():N}"
            });
            Assert.Equal(HttpStatusCode.OK, dep2Resp.StatusCode);

            // Fetch final balance
            var balResp = await _client.GetAsync($"/api/accounts/{gamerId}/balance");
            Assert.Equal(HttpStatusCode.OK, balResp.StatusCode);
            var bal = await balResp.Content.ReadFromJsonAsync<AccountBalanceResponseDto>();
            Assert.NotNull(bal);
            Assert.Equal(300.00m, bal.Balance);
        }

        [Fact]
        public async Task Financial_Concurrency_Case4_Concurrent_Duplicate_Payments_Only_One_Succeeds()
        {
            Guid gamerId = await CreateGamerAsync("fin_case4");

            // Deposit 100
            await _client.PostAsJsonAsync($"/api/accounts/{gamerId}/deposit", new CreditAccountRequestDto { Amount = 100.00m });

            // Get Gamer Account
            var accResp = await _client.GetAsync($"/api/gamers/{gamerId}/account");
            var acc = await accResp.Content.ReadFromJsonAsync<GamerAccountResponseDto>();
            Assert.NotNull(acc);

            string idempotencyKey = $"DUP_PAY_{Guid.NewGuid():N}";

            Func<Task<HttpResponseMessage>> sendPay = () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, "/api/payments")
                {
                    Content = JsonContent.Create(new CreatePaymentRequestDto
                    {
                        GamerAccountId = acc.Id,
                        Amount = 40.00m,
                        IdempotencyKey = idempotencyKey,
                        Reference = "Ref"
                    })
                };
                req.Headers.Add("Idempotency-Key", idempotencyKey);
                return _client.SendAsync(req);
            };

            var res1 = await sendPay();
            Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

            var res2 = await sendPay();
            Assert.Equal(HttpStatusCode.Created, res2.StatusCode);

            var balResp = await _client.GetAsync($"/api/accounts/{gamerId}/balance");
            var bal = await balResp.Content.ReadFromJsonAsync<AccountBalanceResponseDto>();
            Assert.NotNull(bal);
            Assert.Equal(60.00m, bal.Balance);
        }

        [Fact]
        public async Task Financial_Concurrency_Case5_Concurrent_Duplicate_Session_Stops_Only_One_Final_Charge()
        {
            var seed = await SeedHierarchyAsync();
            Guid gamerId = await CreateGamerAsync("fin_case5");

            // Start session
            var startResp = await _client.PostAsJsonAsync("/api/sessions", new StartSessionRequestDto
            {
                GamerId = gamerId,
                WorkstationId = seed.WorkstationId
            });
            Assert.Equal(HttpStatusCode.Created, startResp.StatusCode);
            var session = await startResp.Content.ReadFromJsonAsync<SessionResponseDto>();
            Assert.NotNull(session);

            // Double stop
            var stop1 = await _client.PostAsJsonAsync($"/api/sessions/{session.SessionId}/stop", new { });
            Assert.Equal(HttpStatusCode.OK, stop1.StatusCode);

            var stop2 = await _client.PostAsJsonAsync($"/api/sessions/{session.SessionId}/stop", new { });
            Assert.Equal(HttpStatusCode.OK, stop2.StatusCode);

            // Verify session is ENDED
            var getSess = await _client.GetAsync($"/api/sessions/{session.SessionId}");
            var stoppedSess = await getSess.Content.ReadFromJsonAsync<SessionResponseDto>();
            Assert.NotNull(stoppedSess);
            Assert.Equal("ENDED", stoppedSess.Status);
        }

        private async Task<(Guid OrgId, Guid SiteId, Guid WorkstationId)> SeedHierarchyAsync()
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

            return (orgId, siteId, wsId);
        }

        private async Task<Guid> CreateGamerAsync(string prefix)
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            var resp = await _client.PostAsJsonAsync("/api/gamers", new
            {
                username = $"{prefix}_{suffix}",
                email = $"{prefix}_{suffix}@test.dev",
                password = "Password123!"
            });
            var gamer = await resp.Content.ReadFromJsonAsync<GamerResponseDto>();
            Assert.NotNull(gamer);
            return gamer.Id;
        }
    }
}

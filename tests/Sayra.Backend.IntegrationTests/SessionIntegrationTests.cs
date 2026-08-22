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
    public class SessionIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public SessionIntegrationTests(WebApplicationFactory<Program> factory)
        {
            EnvLoader.Load();
            TestAdminSeeder.EnsureAdminUserCreatedAsync(factory).GetAwaiter().GetResult();
            _client = factory.CreateClient();
            _client.DefaultRequestHeaders.Add("X-User-Id", TestAdminSeeder.AdminUserId.ToString());
            _client.DefaultRequestHeaders.Add("X-User-Role", "Administrator");
        }

        [Fact]
        public async Task Full_Session_Lifecycle_Flow()
        {
            var seed = await SeedHierarchyAndGamerAsync();

            // 1. Start Session
            var startRequest = new StartSessionRequestDto
            {
                GamerId = seed.GamerId,
                WorkstationId = seed.WorkstationId
            };

            var startResponse = await _client.PostAsJsonAsync("/api/sessions", startRequest);
            if (!startResponse.IsSuccessStatusCode)
            {
                var errContent = await startResponse.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Post /api/sessions failed with {startResponse.StatusCode}: {errContent}");
            }
            Assert.Equal(HttpStatusCode.Created, startResponse.StatusCode);

            var createdSession = await startResponse.Content.ReadFromJsonAsync<SessionResponseDto>();
            Assert.NotNull(createdSession);
            Assert.NotEqual(Guid.Empty, createdSession.SessionId);
            Assert.Equal("ACTIVE", createdSession.Status);
            Assert.Equal(seed.GamerId, createdSession.GamerId);
            Assert.Equal(seed.WorkstationId, createdSession.WorkstationId);
            Assert.Equal(seed.OrgId, createdSession.OrganizationId);
            Assert.Equal(seed.SiteId, createdSession.SiteId);

            // 2. Fetch Session by ID
            var getResponse = await _client.GetAsync($"/api/sessions/{createdSession.SessionId}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            var fetchedSession = await getResponse.Content.ReadFromJsonAsync<SessionResponseDto>();
            Assert.NotNull(fetchedSession);
            Assert.Equal(createdSession.SessionId, fetchedSession.SessionId);

            // 3. Query Active Session by Workstation
            var activeWsResponse = await _client.GetAsync($"/api/sessions/workstation/{seed.WorkstationId}/active");
            Assert.Equal(HttpStatusCode.OK, activeWsResponse.StatusCode);
            var activeWsSession = await activeWsResponse.Content.ReadFromJsonAsync<SessionResponseDto>();
            Assert.NotNull(activeWsSession);
            Assert.Equal(createdSession.SessionId, activeWsSession.SessionId);

            // 4. Query Active Session by Gamer
            var activeGamerResponse = await _client.GetAsync($"/api/sessions/gamer/{seed.GamerId}/active");
            Assert.Equal(HttpStatusCode.OK, activeGamerResponse.StatusCode);
            var activeGamerSession = await activeGamerResponse.Content.ReadFromJsonAsync<SessionResponseDto>();
            Assert.NotNull(activeGamerSession);
            Assert.Equal(createdSession.SessionId, activeGamerSession.SessionId);

            // 5. Pause Session
            var pauseResponse = await _client.PostAsJsonAsync($"/api/sessions/{createdSession.SessionId}/pause", new { });
            Assert.Equal(HttpStatusCode.OK, pauseResponse.StatusCode);
            var pausedSession = await pauseResponse.Content.ReadFromJsonAsync<SessionResponseDto>();
            Assert.NotNull(pausedSession);
            Assert.Equal("PAUSED", pausedSession.Status);
            Assert.NotNull(pausedSession.PausedAt);

            // 6. Resume Session
            var resumeResponse = await _client.PostAsJsonAsync($"/api/sessions/{createdSession.SessionId}/resume", new { });
            Assert.Equal(HttpStatusCode.OK, resumeResponse.StatusCode);
            var resumedSession = await resumeResponse.Content.ReadFromJsonAsync<SessionResponseDto>();
            Assert.NotNull(resumedSession);
            Assert.Equal("ACTIVE", resumedSession.Status);
            Assert.Null(resumedSession.PausedAt);

            // 7. Stop Session
            var stopResponse = await _client.PostAsJsonAsync($"/api/sessions/{createdSession.SessionId}/stop", new { });
            if (!stopResponse.IsSuccessStatusCode)
            {
                var errContent = await stopResponse.Content.ReadAsStringAsync();
                Assert.Fail($"Stop session returned {stopResponse.StatusCode}: {errContent}");
            }
            Assert.Equal(HttpStatusCode.OK, stopResponse.StatusCode);
            var stoppedSession = await stopResponse.Content.ReadFromJsonAsync<SessionResponseDto>();
            Assert.NotNull(stoppedSession);
            Assert.Equal("ENDED", stoppedSession.Status);
            Assert.NotNull(stoppedSession.EndedAt);

            // 8. Query Active Session -> Should be 404 (no active session left)
            var noActiveResponse = await _client.GetAsync($"/api/sessions/workstation/{seed.WorkstationId}/active");
            Assert.Equal(HttpStatusCode.NotFound, noActiveResponse.StatusCode);
        }

        [Fact]
        public async Task Session_Timing_Query_Flow()
        {
            var seed = await SeedHierarchyAndGamerAsync();

            var startResp = await _client.PostAsJsonAsync("/api/sessions", new StartSessionRequestDto
            {
                GamerId = seed.GamerId,
                WorkstationId = seed.WorkstationId
            });
            Assert.Equal(HttpStatusCode.Created, startResp.StatusCode);
            var session = await startResp.Content.ReadFromJsonAsync<SessionResponseDto>();
            Assert.NotNull(session);

            // Fetch timing
            var timingResp = await _client.GetAsync($"/api/sessions/{session.SessionId}/timing");
            Assert.Equal(HttpStatusCode.OK, timingResp.StatusCode);

            var timing = await timingResp.Content.ReadFromJsonAsync<SessionTimingResponseDto>();
            Assert.NotNull(timing);
            Assert.Equal(session.SessionId, timing.SessionId);
            Assert.True(timing.ConsumedDuration >= TimeSpan.Zero);
            Assert.Equal(TimeSpan.Zero, timing.PausedDuration);
        }

        [Fact]
        public async Task Session_Duplicate_Pause_And_Resume_Idempotency_Flow()
        {
            var seed = await SeedHierarchyAndGamerAsync();

            var startResp = await _client.PostAsJsonAsync("/api/sessions", new StartSessionRequestDto
            {
                GamerId = seed.GamerId,
                WorkstationId = seed.WorkstationId
            });
            Assert.Equal(HttpStatusCode.Created, startResp.StatusCode);
            var session = await startResp.Content.ReadFromJsonAsync<SessionResponseDto>();
            Assert.NotNull(session);

            // Pause first time
            var pause1 = await _client.PostAsJsonAsync($"/api/sessions/{session.SessionId}/pause", new { });
            Assert.Equal(HttpStatusCode.OK, pause1.StatusCode);

            // Pause second time (idempotent duplicate pause)
            var pause2 = await _client.PostAsJsonAsync($"/api/sessions/{session.SessionId}/pause", new { });
            Assert.Equal(HttpStatusCode.OK, pause2.StatusCode);

            // Resume first time
            var resume1 = await _client.PostAsJsonAsync($"/api/sessions/{session.SessionId}/resume", new { });
            Assert.Equal(HttpStatusCode.OK, resume1.StatusCode);

            // Resume second time (idempotent duplicate resume)
            var resume2 = await _client.PostAsJsonAsync($"/api/sessions/{session.SessionId}/resume", new { });
            Assert.Equal(HttpStatusCode.OK, resume2.StatusCode);

            // Verify timing query returns valid active consumed duration
            var timingResp = await _client.GetAsync($"/api/sessions/{session.SessionId}/timing");
            Assert.Equal(HttpStatusCode.OK, timingResp.StatusCode);
            var timing = await timingResp.Content.ReadFromJsonAsync<SessionTimingResponseDto>();
            Assert.NotNull(timing);
            Assert.True(timing.ConsumedDuration >= TimeSpan.Zero);
        }

        [Fact]
        public async Task Double_Start_Session_On_Workstation_Should_Return_Conflict()
        {
            var seed = await SeedHierarchyAndGamerAsync();

            // First start session succeeds
            var req1 = new StartSessionRequestDto
            {
                GamerId = seed.GamerId,
                WorkstationId = seed.WorkstationId
            };
            var resp1 = await _client.PostAsJsonAsync("/api/sessions", req1);
            Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);

            // Second gamer tries to start session on same workstation
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            var gamer2Resp = await _client.PostAsJsonAsync("/api/gamers", new
            {
                username = $"gamer2_{suffix}",
                email = $"gamer2_{suffix}@test.dev",
                password = "Password123!"
            });
            Assert.Equal(HttpStatusCode.Created, gamer2Resp.StatusCode);
            using var gamer2Doc = JsonDocument.Parse(await gamer2Resp.Content.ReadAsStringAsync());
            Guid gamer2Id = gamer2Doc.RootElement.GetProperty("id").GetGuid();

            var req2 = new StartSessionRequestDto
            {
                GamerId = gamer2Id,
                WorkstationId = seed.WorkstationId
            };
            var resp2 = await _client.PostAsJsonAsync("/api/sessions", req2);
            Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);
        }

        [Fact]
        public async Task Session_With_Reservation_Lifecycle_Flow()
        {
            var seed = await SeedHierarchyAndGamerAsync();

            // 1. Create and Confirm Reservation
            var now = DateTime.UtcNow;
            var createResResponse = await _client.PostAsJsonAsync("/api/reservations", new CreateReservationRequestDto
            {
                GamerId = seed.GamerId,
                SiteId = seed.SiteId,
                WorkstationId = seed.WorkstationId,
                StartTimeUtc = now.AddMinutes(5),
                EndTimeUtc = now.AddHours(2),
                ReservedAmount = 20.00m
            });
            Assert.Equal(HttpStatusCode.Created, createResResponse.StatusCode);
            var resDto = await createResResponse.Content.ReadFromJsonAsync<ReservationResponseDto>();
            Assert.NotNull(resDto);

            var confirmResResponse = await _client.PostAsJsonAsync($"/api/reservations/{resDto.ReservationId}/confirm", new { });
            Assert.Equal(HttpStatusCode.OK, confirmResResponse.StatusCode);

            // 2. Start Session with ReservationId
            var startReq = new StartSessionRequestDto
            {
                GamerId = seed.GamerId,
                WorkstationId = seed.WorkstationId,
                ReservationId = resDto.ReservationId
            };

            var startResp = await _client.PostAsJsonAsync("/api/sessions", startReq);
            Assert.Equal(HttpStatusCode.Created, startResp.StatusCode);
            var sessionDto = await startResp.Content.ReadFromJsonAsync<SessionResponseDto>();
            Assert.NotNull(sessionDto);
            Assert.Equal(resDto.ReservationId, sessionDto.ReservationId);

            // Verify timing query returns allocated remaining duration from reservation
            var timingResp = await _client.GetAsync($"/api/sessions/{sessionDto.SessionId}/timing");
            Assert.Equal(HttpStatusCode.OK, timingResp.StatusCode);
            var timing = await timingResp.Content.ReadFromJsonAsync<SessionTimingResponseDto>();
            Assert.NotNull(timing);
            Assert.NotNull(timing.RemainingDuration);
            Assert.NotNull(timing.ExpirationTimeUtc);

            // 3. Verify Reservation status is now ACTIVE
            var getResResponse = await _client.GetAsync($"/api/reservations/{resDto.ReservationId}");
            Assert.Equal(HttpStatusCode.OK, getResResponse.StatusCode);
            var activeResDto = await getResResponse.Content.ReadFromJsonAsync<ReservationResponseDto>();
            Assert.NotNull(activeResDto);
            Assert.Equal("ACTIVE", activeResDto.Status);

            // 4. Stop Session
            var stopResp = await _client.PostAsJsonAsync($"/api/sessions/{sessionDto.SessionId}/stop", new { });
            Assert.Equal(HttpStatusCode.OK, stopResp.StatusCode);

            // 5. Verify Reservation status is now COMPLETED
            var getResCompletedResponse = await _client.GetAsync($"/api/reservations/{resDto.ReservationId}");
            Assert.Equal(HttpStatusCode.OK, getResCompletedResponse.StatusCode);
            var completedResDto = await getResCompletedResponse.Content.ReadFromJsonAsync<ReservationResponseDto>();
            Assert.NotNull(completedResDto);
            Assert.Equal("COMPLETED", completedResDto.Status);
        }

        [Fact]
        public async Task Double_Stop_Session_Should_Be_Idempotent()
        {
            var seed = await SeedHierarchyAndGamerAsync();

            var startResp = await _client.PostAsJsonAsync("/api/sessions", new StartSessionRequestDto
            {
                GamerId = seed.GamerId,
                WorkstationId = seed.WorkstationId
            });
            Assert.Equal(HttpStatusCode.Created, startResp.StatusCode);
            var sessionDto = await startResp.Content.ReadFromJsonAsync<SessionResponseDto>();
            Assert.NotNull(sessionDto);

            // Stop first time
            var stop1 = await _client.PostAsJsonAsync($"/api/sessions/{sessionDto.SessionId}/stop", new { });
            Assert.Equal(HttpStatusCode.OK, stop1.StatusCode);

            // Stop second time
            var stop2 = await _client.PostAsJsonAsync($"/api/sessions/{sessionDto.SessionId}/stop", new { });
            Assert.Equal(HttpStatusCode.OK, stop2.StatusCode);
        }

        [Fact]
        public async Task Invalid_State_Transition_Should_Return_BadRequest()
        {
            var seed = await SeedHierarchyAndGamerAsync();

            var startResp = await _client.PostAsJsonAsync("/api/sessions", new StartSessionRequestDto
            {
                GamerId = seed.GamerId,
                WorkstationId = seed.WorkstationId
            });
            Assert.Equal(HttpStatusCode.Created, startResp.StatusCode);
            var sessionDto = await startResp.Content.ReadFromJsonAsync<SessionResponseDto>();
            Assert.NotNull(sessionDto);

            // Stop session (status -> ENDED)
            var stopResp = await _client.PostAsJsonAsync($"/api/sessions/{sessionDto.SessionId}/stop", new { });
            Assert.Equal(HttpStatusCode.OK, stopResp.StatusCode);

            // Try to pause an ENDED session -> should return 400 Bad Request
            var pauseResp = await _client.PostAsJsonAsync($"/api/sessions/{sessionDto.SessionId}/pause", new { });
            Assert.Equal(HttpStatusCode.BadRequest, pauseResp.StatusCode);
        }

        private async Task<(Guid OrgId, Guid SiteId, Guid WorkstationId, Guid GamerId)> SeedHierarchyAndGamerAsync()
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);

            // Create Organization
            var createOrgResponse = await _client.PostAsJsonAsync("/api/organizations", new
            {
                code = $"ORG_{suffix}",
                name = $"Organization {suffix}"
            });
            Assert.Equal(HttpStatusCode.Created, createOrgResponse.StatusCode);
            using var orgDoc = JsonDocument.Parse(await createOrgResponse.Content.ReadAsStringAsync());
            Guid orgId = orgDoc.RootElement.GetProperty("id").GetGuid();

            // Create Site
            var createSiteResponse = await _client.PostAsJsonAsync("/api/sites", new
            {
                organizationId = orgId,
                code = $"SITE_{suffix}",
                name = $"Site {suffix}",
                timezone = "UTC"
            });
            Assert.Equal(HttpStatusCode.Created, createSiteResponse.StatusCode);
            using var siteDoc = JsonDocument.Parse(await createSiteResponse.Content.ReadAsStringAsync());
            Guid siteId = siteDoc.RootElement.GetProperty("id").GetGuid();

            // Create Zone
            var createZoneResponse = await _client.PostAsJsonAsync("/api/zones", new
            {
                siteId = siteId,
                code = $"ZONE_{suffix}",
                name = $"Zone {suffix}"
            });
            Assert.Equal(HttpStatusCode.Created, createZoneResponse.StatusCode);
            using var zoneDoc = JsonDocument.Parse(await createZoneResponse.Content.ReadAsStringAsync());
            Guid zoneId = zoneDoc.RootElement.GetProperty("id").GetGuid();

            // Register Workstation
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
            Assert.Equal(HttpStatusCode.OK, regWsResponse.StatusCode);
            using var wsDoc = JsonDocument.Parse(await regWsResponse.Content.ReadAsStringAsync());
            Guid wsId = wsDoc.RootElement.GetProperty("id").GetGuid();

            // Assign Workstation
            var assignResponse = await _client.PostAsJsonAsync($"/api/workstations/{wsId}/assignment", new
            {
                organizationId = orgId,
                siteId = siteId,
                zoneId = zoneId
            });
            Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);

            // Create Gamer
            var createGamerResponse = await _client.PostAsJsonAsync("/api/gamers", new
            {
                username = $"gamer_{suffix}",
                email = $"gamer_{suffix}@test.dev",
                password = "Password123!"
            });
            Assert.Equal(HttpStatusCode.Created, createGamerResponse.StatusCode);
            using var gamerDoc = JsonDocument.Parse(await createGamerResponse.Content.ReadAsStringAsync());
            Guid gamerId = gamerDoc.RootElement.GetProperty("id").GetGuid();

            return (orgId, siteId, wsId, gamerId);
        }
    }
}

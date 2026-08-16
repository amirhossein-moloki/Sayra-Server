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
    public class ReservationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public ReservationIntegrationTests(WebApplicationFactory<Program> factory)
        {
            EnvLoader.Load();
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task Full_Reservation_Lifecycle_And_Validation_Flow()
        {
            var seed = await SeedHierarchyAndGamerAsync();

            var now = DateTime.UtcNow;
            var startTime = now.AddHours(1);
            var endTime = now.AddHours(3);

            // 1. Create Reservation
            var createRequest = new CreateReservationRequestDto
            {
                GamerId = seed.GamerId,
                SiteId = seed.SiteId,
                WorkstationId = seed.WorkstationId,
                StartTimeUtc = startTime,
                EndTimeUtc = endTime,
                ReservedAmount = 25.00m
            };

            var createResponse = await _client.PostAsJsonAsync("/api/reservations", createRequest);
            if (!createResponse.IsSuccessStatusCode)
            {
                var errContent = await createResponse.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Post /api/reservations failed with {createResponse.StatusCode}: {errContent}");
            }
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            var createdReservation = await createResponse.Content.ReadFromJsonAsync<ReservationResponseDto>();
            Assert.NotNull(createdReservation);
            Assert.NotEqual(Guid.Empty, createdReservation.ReservationId);
            Assert.Equal("PENDING", createdReservation.Status);
            Assert.Equal(seed.GamerId, createdReservation.GamerId);
            Assert.Equal(seed.SiteId, createdReservation.SiteId);
            Assert.Equal(seed.WorkstationId, createdReservation.WorkstationId);
            Assert.Equal(25.00m, createdReservation.ReservedAmount);

            // 2. Fetch Reservation by ID
            var getResponse = await _client.GetAsync($"/api/reservations/{createdReservation.ReservationId}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            var fetchedReservation = await getResponse.Content.ReadFromJsonAsync<ReservationResponseDto>();
            Assert.NotNull(fetchedReservation);
            Assert.Equal(createdReservation.ReservationId, fetchedReservation.ReservationId);

            // 3. Confirm Reservation
            var confirmResponse = await _client.PostAsJsonAsync($"/api/reservations/{createdReservation.ReservationId}/confirm", new { });
            Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
            var confirmedReservation = await confirmResponse.Content.ReadFromJsonAsync<ReservationResponseDto>();
            Assert.NotNull(confirmedReservation);
            Assert.Equal("CONFIRMED", confirmedReservation.Status);

            // 4. Activate Reservation
            var activateResponse = await _client.PostAsJsonAsync($"/api/reservations/{createdReservation.ReservationId}/activate", new { });
            Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
            var activatedReservation = await activateResponse.Content.ReadFromJsonAsync<ReservationResponseDto>();
            Assert.NotNull(activatedReservation);
            Assert.Equal("ACTIVE", activatedReservation.Status);

            // 5. Validate Active Reservation
            var validateUrl = $"/api/reservations/validate?reservationId={createdReservation.ReservationId}&gamerId={seed.GamerId}&siteId={seed.SiteId}&workstationId={seed.WorkstationId}";
            var validateResponse = await _client.GetAsync(validateUrl);
            Assert.Equal(HttpStatusCode.OK, validateResponse.StatusCode);
            var valResult = await validateResponse.Content.ReadFromJsonAsync<ReservationValidationResultDto>();
            Assert.NotNull(valResult);
            Assert.True(valResult.IsValid);
            Assert.Equal("VALID", valResult.Code);

            // 6. Cancel Reservation
            var cancelResponse = await _client.PostAsJsonAsync($"/api/reservations/{createdReservation.ReservationId}/cancel", new { reason = "Testing cancellation" });
            Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
            var cancelledReservation = await cancelResponse.Content.ReadFromJsonAsync<ReservationResponseDto>();
            Assert.NotNull(cancelledReservation);
            Assert.Equal("CANCELLED", cancelledReservation.Status);

            // 7. Re-validate Cancelled Reservation -> should be invalid
            var revalidateResponse = await _client.GetAsync(validateUrl);
            Assert.Equal(HttpStatusCode.OK, revalidateResponse.StatusCode);
            var revalResult = await revalidateResponse.Content.ReadFromJsonAsync<ReservationValidationResultDto>();
            Assert.NotNull(revalResult);
            Assert.False(revalResult.IsValid);
            Assert.Equal("RESERVATION_CONSUMED", revalResult.Code);
        }

        [Fact]
        public async Task Overlapping_Workstation_Reservations_Should_Return_Conflict()
        {
            var seed = await SeedHierarchyAndGamerAsync();

            var now = DateTime.UtcNow;

            // First reservation: 10:00 - 12:00
            var res1Request = new CreateReservationRequestDto
            {
                GamerId = seed.GamerId,
                SiteId = seed.SiteId,
                WorkstationId = seed.WorkstationId,
                StartTimeUtc = now.AddHours(1),
                EndTimeUtc = now.AddHours(3),
                ReservedAmount = 10.00m
            };

            var res1Response = await _client.PostAsJsonAsync("/api/reservations", res1Request);
            Assert.Equal(HttpStatusCode.Created, res1Response.StatusCode);

            // Second reservation overlapping: 11:00 - 13:00
            var res2Request = new CreateReservationRequestDto
            {
                GamerId = seed.GamerId,
                SiteId = seed.SiteId,
                WorkstationId = seed.WorkstationId,
                StartTimeUtc = now.AddHours(2),
                EndTimeUtc = now.AddHours(4),
                ReservedAmount = 10.00m
            };

            var res2Response = await _client.PostAsJsonAsync("/api/reservations", res2Request);
            Assert.Equal(HttpStatusCode.Conflict, res2Response.StatusCode);
        }

        [Fact]
        public async Task CreateReservation_Invalid_GamerId_Should_Return_BadRequest()
        {
            var seed = await SeedHierarchyAndGamerAsync();

            var request = new CreateReservationRequestDto
            {
                GamerId = Guid.NewGuid(), // Non-existent
                SiteId = seed.SiteId,
                WorkstationId = seed.WorkstationId,
                StartTimeUtc = DateTime.UtcNow.AddHours(1),
                EndTimeUtc = DateTime.UtcNow.AddHours(2)
            };

            var response = await _client.PostAsJsonAsync("/api/reservations", request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Sayra.Backend.Api;
using Sayra.Backend.Application.Pricing;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Persistence;

namespace Sayra.Backend.IntegrationTests
{
    public class BillingIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public BillingIntegrationTests(WebApplicationFactory<Program> factory)
        {
            EnvLoader.Load();
            _factory = factory;
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task Full_Session_Billing_Calculation_And_Persistence_Flow()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rateSnapshotService = scope.ServiceProvider.GetRequiredService<IRateSnapshotService>();

            // 1. Setup Organization, Site, Gamer, Workstation
            var org = new Organization { Name = "Billing Test Org", Code = $"ORG-BIL-{Guid.NewGuid():N}"[..15] };
            db.Organizations.Add(org);

            var site = new Site { OrganizationId = org.Id, Code = $"S-{Guid.NewGuid():N}"[..8], Name = "Billing Test Site" };
            db.Sites.Add(site);

            var plan = new PricingPlan { SiteId = site.Id, Name = "Billing Test Plan", Status = "Active" };
            db.PricingPlans.Add(plan);

            var rule = new PricingRule { PricingPlanId = plan.PricingPlanId, Name = "Billing Test Rule", RateAmount = 30000.0000m, Priority = 1 };
            db.PricingRules.Add(rule);

            var gamer = new Gamer { GamerId = $"GMR-{Guid.NewGuid():N}"[..8], Username = $"usr_{Guid.NewGuid():N}"[..8], Email = $"usr_{Guid.NewGuid():N}@test.com" };
            db.Gamers.Add(gamer);

            var ws = new Workstation { PcId = $"PC-{Guid.NewGuid():N}"[..8], SiteId = site.Code, Hostname = "H1", MacAddress = $"BB:22:33:44:55:{Random.Shared.Next(10, 99)}", IpAddress = "127.0.0.1" };
            db.Workstations.Add(ws);

            var session = new Session
            {
                OrganizationId = site.OrganizationId,
                SiteId = site.Id,
                WorkstationId = ws.Id,
                GamerId = gamer.Id,
                PricingPlanId = plan.PricingPlanId,
                Status = "ACTIVE",
                StartedAt = DateTime.UtcNow.AddHours(-2) // Started 2 hours ago
            };
            db.Sessions.Add(session);

            // Add active session segment
            var segment = new SessionSegment
            {
                SessionId = session.Id,
                Type = "ACTIVE",
                StartedAtUtc = DateTime.UtcNow.AddHours(-2),
                EndedAtUtc = DateTime.UtcNow
            };
            db.SessionSegments.Add(segment);

            await db.SaveChangesAsync();

            // 2. Create Rate Snapshot (Rate = 30000.0000 SAY / hour)
            var rateSnapshot = await rateSnapshotService.CreateSnapshotAsync(
                session.Id, plan.PricingPlanId, rule.PricingRuleId, 30000.0000m, "SAY", "Billing Test Rule", DateTime.UtcNow);
            Assert.NotNull(rateSnapshot);

            // 3. Calculate Billing via API
            var calcReq = new CalculateSessionBillingRequestDto
            {
                DiscountAmount = 5000m,
                AdjustmentAmount = 1000m
            };

            var calcRes = await _client.PostAsJsonAsync($"/api/sessions/{session.Id}/billing/calculate", calcReq);
            Assert.Equal(HttpStatusCode.Created, calcRes.StatusCode);

            var billingDto = await calcRes.Content.ReadFromJsonAsync<BillingResultResponseDto>();
            Assert.NotNull(billingDto);
            Assert.Equal(session.Id, billingDto.SessionId);
            Assert.Equal(rateSnapshot.RateSnapshotId, billingDto.RateSnapshotId);

            // 2 hours @ 30000 = 60000 Subtotal; Discount 5000, Adjustment 1000 => Final 56000
            Assert.Equal(60000m, billingDto.Subtotal);
            Assert.Equal(5000m, billingDto.DiscountAmount);
            Assert.Equal(1000m, billingDto.AdjustmentAmount);
            Assert.Equal(56000m, billingDto.FinalAmount);

            // 4. Get Billing Result by ID via GET /api/billing/{id}
            var getRes = await _client.GetAsync($"/api/billing/{billingDto.BillingResultId}");
            Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
            var getDto = await getRes.Content.ReadFromJsonAsync<BillingResultResponseDto>();
            Assert.NotNull(getDto);
            Assert.Equal(billingDto.BillingResultId, getDto.BillingResultId);
            Assert.Equal(56000m, getDto.FinalAmount);

            // 5. Get Latest Session Billing via GET /api/sessions/{id}/billing
            var latestRes = await _client.GetAsync($"/api/sessions/{session.Id}/billing");
            Assert.Equal(HttpStatusCode.OK, latestRes.StatusCode);
            var latestDto = await latestRes.Content.ReadFromJsonAsync<BillingResultResponseDto>();
            Assert.NotNull(latestDto);
            Assert.Equal(billingDto.BillingResultId, latestDto.BillingResultId);

            // 6. Get Billing History via GET /api/sessions/{id}/billing/history
            var historyRes = await _client.GetAsync($"/api/sessions/{session.Id}/billing/history");
            Assert.Equal(HttpStatusCode.OK, historyRes.StatusCode);
            var historyList = await historyRes.Content.ReadFromJsonAsync<List<BillingResultResponseDto>>();
            Assert.NotNull(historyList);
            Assert.Single(historyList);

            // 7. Direct Database Inspection
            var dbBilling = await db.BillingResults.FirstOrDefaultAsync(b => b.Id == billingDto.BillingResultId);
            Assert.NotNull(dbBilling);
            Assert.Equal(60000m, dbBilling.Subtotal.Amount);
            Assert.Equal(56000m, dbBilling.FinalAmount.Amount);
            Assert.Equal("SAY", dbBilling.Currency);
        }

        [Fact]
        public async Task CalculateBilling_ForNonExistentSession_ReturnsNotFound()
        {
            var nonExistentSessionId = Guid.NewGuid();
            var calcRes = await _client.PostAsJsonAsync($"/api/sessions/{nonExistentSessionId}/billing/calculate", new CalculateSessionBillingRequestDto());
            Assert.Equal(HttpStatusCode.NotFound, calcRes.StatusCode);
        }

        [Fact]
        public async Task CalculateBilling_ForSessionWithoutRateSnapshot_ReturnsNotFoundWithErrorCode()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var org = new Organization { Name = "No Snap Org", Code = $"ORG-NS-{Guid.NewGuid():N}"[..15] };
            db.Organizations.Add(org);

            var site = new Site { OrganizationId = org.Id, Code = $"S-{Guid.NewGuid():N}"[..8], Name = "No Snap Site" };
            db.Sites.Add(site);

            var gamer = new Gamer { GamerId = $"GMR-{Guid.NewGuid():N}"[..8], Username = $"usr_{Guid.NewGuid():N}"[..8], Email = $"usr_{Guid.NewGuid():N}@test.com" };
            db.Gamers.Add(gamer);

            var ws = new Workstation { PcId = $"PC-{Guid.NewGuid():N}"[..8], SiteId = site.Code, Hostname = "H1", MacAddress = $"BB:33:44:55:66:{Random.Shared.Next(10, 99)}", IpAddress = "127.0.0.1" };
            db.Workstations.Add(ws);

            var session = new Session
            {
                OrganizationId = site.OrganizationId,
                SiteId = site.Id,
                WorkstationId = ws.Id,
                GamerId = gamer.Id,
                Status = "ACTIVE"
            };
            db.Sessions.Add(session);
            await db.SaveChangesAsync();

            // Act: Calculate billing without creating rate snapshot
            var calcRes = await _client.PostAsJsonAsync($"/api/sessions/{session.Id}/billing/calculate", new CalculateSessionBillingRequestDto());
            Assert.Equal(HttpStatusCode.NotFound, calcRes.StatusCode);
        }
    }
}

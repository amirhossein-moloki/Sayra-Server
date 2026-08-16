using System;
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
    public class PricingIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public PricingIntegrationTests(WebApplicationFactory<Program> factory)
        {
            EnvLoader.Load();
            _factory = factory;
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task Full_Pricing_Plan_Rules_Activation_And_Rate_Resolution_Flow()
        {
            // 1. Create Organization and Site
            var orgReq = new CreateOrganizationRequestDto
            {
                Name = "Pricing Test Org",
                Code = $"ORG-PRC-{Guid.NewGuid():N}"[..15]
            };
            var orgRes = await _client.PostAsJsonAsync("/api/organizations", orgReq);
            Assert.Equal(HttpStatusCode.Created, orgRes.StatusCode);
            var orgDto = await orgRes.Content.ReadFromJsonAsync<OrganizationResponseDto>();

            var siteReq = new CreateSiteRequestDto
            {
                OrganizationId = orgDto!.Id,
                Name = "Pricing Test Site",
                Code = $"STE-{Guid.NewGuid():N}"[..8],
                Timezone = "UTC"
            };
            var siteRes = await _client.PostAsJsonAsync("/api/sites", siteReq);
            Assert.Equal(HttpStatusCode.Created, siteRes.StatusCode);
            var siteDto = await siteRes.Content.ReadFromJsonAsync<SiteResponseDto>();

            // 2. Create Pricing Plan
            var planReq = new CreatePricingPlanRequestDto
            {
                SiteId = siteDto!.Id,
                Name = "Cyber Standard Plan",
                Currency = "SAY"
            };
            var planRes = await _client.PostAsJsonAsync("/api/pricing/plans", planReq);
            Assert.Equal(HttpStatusCode.Created, planRes.StatusCode);
            var planDto = await planRes.Content.ReadFromJsonAsync<PricingPlanResponseDto>();
            Assert.NotNull(planDto);
            Assert.Equal("Inactive", planDto.Status);

            // 3. Create Pricing Rules (Priority 1: VIP Gamer rule, Priority 2: Site Default rule)
            var rule1Req = new CreatePricingRuleRequestDto
            {
                Name = "VIP Gamer Discount",
                RateAmount = 12.5000m,
                Currency = "SAY",
                Priority = 1,
                GamerType = "VIP"
            };
            var rule1Res = await _client.PostAsJsonAsync($"/api/pricing/plans/{planDto.PricingPlanId}/rules", rule1Req);
            Assert.Equal(HttpStatusCode.Created, rule1Res.StatusCode);

            var rule2Req = new CreatePricingRuleRequestDto
            {
                Name = "Standard Default Rate",
                RateAmount = 20.0000m,
                Currency = "SAY",
                Priority = 2
            };
            var rule2Res = await _client.PostAsJsonAsync($"/api/pricing/plans/{planDto.PricingPlanId}/rules", rule2Req);
            Assert.Equal(HttpStatusCode.Created, rule2Res.StatusCode);

            // 4. Activate Plan
            var actRes = await _client.PostAsync($"/api/pricing/plans/{planDto.PricingPlanId}/activate", null);
            Assert.Equal(HttpStatusCode.OK, actRes.StatusCode);
            var actDto = await actRes.Content.ReadFromJsonAsync<PricingPlanResponseDto>();
            Assert.Equal("Active", actDto!.Status);

            // 5. Resolve Rate for VIP Gamer (Should match Priority 1 rule = 12.5000 SAY)
            var vipResolveRes = await _client.GetAsync($"/api/pricing/resolve?siteId={siteDto.Id}&gamerType=VIP");
            Assert.Equal(HttpStatusCode.OK, vipResolveRes.StatusCode);
            var vipRateDto = await vipResolveRes.Content.ReadFromJsonAsync<ResolvedRateResponseDto>();
            Assert.NotNull(vipRateDto);
            Assert.Equal(12.5000m, vipRateDto.RateAmount);
            Assert.Equal("VIP Gamer Discount", vipRateDto.RuleReference);

            // 6. Resolve Rate for Standard Gamer (Should match Priority 2 default rule = 20.0000 SAY)
            var stdResolveRes = await _client.GetAsync($"/api/pricing/resolve?siteId={siteDto.Id}&gamerType=STANDARD");
            Assert.Equal(HttpStatusCode.OK, stdResolveRes.StatusCode);
            var stdRateDto = await stdResolveRes.Content.ReadFromJsonAsync<ResolvedRateResponseDto>();
            Assert.NotNull(stdRateDto);
            Assert.Equal(20.0000m, stdRateDto.RateAmount);
            Assert.Equal("Standard Default Rate", stdRateDto.RuleReference);
        }

        [Fact]
        public async Task Duplicate_PricingPlan_Name_For_Same_Site_Should_Return_Conflict()
        {
            var orgReq = new CreateOrganizationRequestDto { Name = "Org DUP", Code = $"ORG-PRCDP-{Guid.NewGuid():N}"[..15] };
            var orgRes = await _client.PostAsJsonAsync("/api/organizations", orgReq);
            var orgDto = await orgRes.Content.ReadFromJsonAsync<OrganizationResponseDto>();

            var siteReq = new CreateSiteRequestDto { OrganizationId = orgDto!.Id, Name = "Site DUP", Code = $"S-{Guid.NewGuid():N}"[..8] };
            var siteRes = await _client.PostAsJsonAsync("/api/sites", siteReq);
            var siteDto = await siteRes.Content.ReadFromJsonAsync<SiteResponseDto>();

            var planReq1 = new CreatePricingPlanRequestDto { SiteId = siteDto!.Id, Name = "Plan Unique" };
            var planReq2 = new CreatePricingPlanRequestDto { SiteId = siteDto.Id, Name = "Plan Unique" };

            var res1 = await _client.PostAsJsonAsync("/api/pricing/plans", planReq1);
            Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

            var res2 = await _client.PostAsJsonAsync("/api/pricing/plans", planReq2);
            Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
        }

        [Fact]
        public async Task Duplicate_PricingRule_Priority_Should_Return_Conflict()
        {
            var orgReq = new CreateOrganizationRequestDto { Name = "Org Rule DUP", Code = $"ORG-RLDP-{Guid.NewGuid():N}"[..15] };
            var orgRes = await _client.PostAsJsonAsync("/api/organizations", orgReq);
            var orgDto = await orgRes.Content.ReadFromJsonAsync<OrganizationResponseDto>();

            var siteReq = new CreateSiteRequestDto { OrganizationId = orgDto!.Id, Name = "Site Rule DUP", Code = $"S-{Guid.NewGuid():N}"[..8] };
            var siteRes = await _client.PostAsJsonAsync("/api/sites", siteReq);
            var siteDto = await siteRes.Content.ReadFromJsonAsync<SiteResponseDto>();

            var planReq = new CreatePricingPlanRequestDto { SiteId = siteDto!.Id, Name = "Rule Dup Plan" };
            var planRes = await _client.PostAsJsonAsync("/api/pricing/plans", planReq);
            var planDto = await planRes.Content.ReadFromJsonAsync<PricingPlanResponseDto>();

            var rule1 = new CreatePricingRuleRequestDto { Name = "R1", RateAmount = 10, Priority = 1 };
            var rule2 = new CreatePricingRuleRequestDto { Name = "R2", RateAmount = 15, Priority = 1 }; // Same priority 1!

            var res1 = await _client.PostAsJsonAsync($"/api/pricing/plans/{planDto!.PricingPlanId}/rules", rule1);
            Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

            var res2 = await _client.PostAsJsonAsync($"/api/pricing/plans/{planDto.PricingPlanId}/rules", rule2);
            Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
        }

        [Fact]
        public async Task RateSnapshot_Persistence_Precision_And_Immutability_In_Database()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var snapshotService = scope.ServiceProvider.GetRequiredService<IRateSnapshotService>();

            var org = new Organization { Name = "DB Org", Code = $"O-{Guid.NewGuid():N}"[..8] };
            db.Organizations.Add(org);

            var site = new Site { OrganizationId = org.Id, Code = $"S-{Guid.NewGuid():N}"[..8], Name = "DB Site" };
            db.Sites.Add(site);

            var plan = new PricingPlan { SiteId = site.Id, Name = "DB Plan", Status = "Active" };
            db.PricingPlans.Add(plan);

            var rule = new PricingRule { PricingPlanId = plan.PricingPlanId, Name = "DB Rule", RateAmount = 18.7550m, Priority = 1 };
            db.PricingRules.Add(rule);

            var gamer = new Gamer { GamerId = $"GMR-{Guid.NewGuid():N}"[..8], Username = $"usr_{Guid.NewGuid():N}"[..8], Email = $"usr_{Guid.NewGuid():N}@test.com" };
            db.Gamers.Add(gamer);

            var ws = new Workstation { PcId = $"PC-{Guid.NewGuid():N}"[..8], SiteId = site.Code, Hostname = "H1", MacAddress = $"BB:11:22:33:44:{Random.Shared.Next(10, 99)}", IpAddress = "127.0.0.1" };
            db.Workstations.Add(ws);

            var session = new Session
            {
                OrganizationId = site.OrganizationId,
                SiteId = site.Id,
                WorkstationId = ws.Id,
                GamerId = gamer.Id,
                PricingPlanId = plan.PricingPlanId,
                Status = "ACTIVE"
            };
            db.Sessions.Add(session);
            await db.SaveChangesAsync();

            // Create Snapshot via IRateSnapshotService
            var created = await snapshotService.CreateSnapshotAsync(
                session.Id, plan.PricingPlanId, rule.PricingRuleId, 18.7550m, "SAY", "DB Rule", DateTime.UtcNow);

            Assert.NotNull(created);
            Assert.Equal(18.7550m, created.RateAmount);

            // Fetch directly from DB to verify numeric(18, 4) persistence
            var dbSnapshot = await db.RateSnapshots.FirstOrDefaultAsync(s => s.SessionId == session.Id);
            Assert.NotNull(dbSnapshot);
            Assert.Equal(18.7550m, dbSnapshot.RateAmount);
            Assert.Equal("SAY", dbSnapshot.Currency);

            // Verify immutability: calling CreateSnapshotAsync again with different rate amount does not change stored rate!
            var secondCall = await snapshotService.CreateSnapshotAsync(
                session.Id, plan.PricingPlanId, rule.PricingRuleId, 999.9999m, "SAY", "Tampered Rule", DateTime.UtcNow);

            Assert.Equal(18.7550m, secondCall.RateAmount);
        }
    }
}

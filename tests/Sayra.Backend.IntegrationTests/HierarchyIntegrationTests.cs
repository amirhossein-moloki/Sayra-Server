using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Sayra.Backend.Api;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Persistence;

namespace Sayra.Backend.IntegrationTests
{
    public class HierarchyIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public HierarchyIntegrationTests(WebApplicationFactory<Program> factory)
        {
            EnvLoader.Load();
            _factory = factory;
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task Full_Organization_Site_Zone_WorkstationAssignment_Flow_Should_Succeed()
        {
            // 1. Create Organization
            var orgReq = new CreateOrganizationRequestDto
            {
                Name = "Apex Gaming LLC",
                Code = $"ORG-FLOW-{Guid.NewGuid():N}"[..15]
            };

            var orgRes = await _client.PostAsJsonAsync("/api/organizations", orgReq);
            if (!orgRes.IsSuccessStatusCode)
            {
                var errContent = await orgRes.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Post /api/organizations failed with {orgRes.StatusCode}: {errContent}");
            }
            Assert.Equal(HttpStatusCode.Created, orgRes.StatusCode);
            var orgDto = await orgRes.Content.ReadFromJsonAsync<OrganizationResponseDto>();
            Assert.NotNull(orgDto);
            Assert.True(orgDto.Id != Guid.Empty);

            // 2. Get Organization
            var getOrgRes = await _client.GetAsync($"/api/organizations/{orgDto.Id}");
            Assert.Equal(HttpStatusCode.OK, getOrgRes.StatusCode);

            // 3. Create Site
            var siteReq = new CreateSiteRequestDto
            {
                OrganizationId = orgDto.Id,
                Name = "Downtown Cyber Cafe",
                Code = "SITE-DT-01",
                Timezone = "UTC"
            };

            var siteRes = await _client.PostAsJsonAsync("/api/sites", siteReq);
            Assert.Equal(HttpStatusCode.Created, siteRes.StatusCode);
            var siteDto = await siteRes.Content.ReadFromJsonAsync<SiteResponseDto>();
            Assert.NotNull(siteDto);

            // 4. Create Zone
            var zoneReq = new CreateZoneRequestDto
            {
                SiteId = siteDto.Id,
                Name = "VIP Arena",
                Code = "ZONE-VIP"
            };

            var zoneRes = await _client.PostAsJsonAsync("/api/zones", zoneReq);
            Assert.Equal(HttpStatusCode.Created, zoneRes.StatusCode);
            var zoneDto = await zoneRes.Content.ReadFromJsonAsync<ZoneResponseDto>();
            Assert.NotNull(zoneDto);

            // 5. Register Workstation in DB
            Guid wsId = Guid.NewGuid();
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var workstation = new Workstation
                {
                    PcId = $"PC-HIER-{Guid.NewGuid():N}"[..18],
                    SiteId = siteDto.Code,
                    Hostname = "DESKTOP-HIER",
                    MacAddress = string.Format("00:11:22:{0:X2}:{1:X2}:{2:X2}", Random.Shared.Next(255), Random.Shared.Next(255), Random.Shared.Next(255)),
                    IpAddress = "10.0.0.100",
                    Status = "OFFLINE"
                };
                typeof(BaseEntity).GetProperty("Id")?.SetValue(workstation, wsId);
                await db.Workstations.AddAsync(workstation);
                await db.SaveChangesAsync();
            }

            // 6. Assign Workstation
            var assignReq = new AssignWorkstationRequestDto
            {
                OrganizationId = orgDto.Id,
                SiteId = siteDto.Id,
                ZoneId = zoneDto.Id
            };

            var assignRes = await _client.PostAsJsonAsync($"/api/workstations/{wsId}/assignment", assignReq);
            Assert.Equal(HttpStatusCode.OK, assignRes.StatusCode);
            var assignDto = await assignRes.Content.ReadFromJsonAsync<WorkstationAssignmentResponseDto>();
            Assert.NotNull(assignDto);
            Assert.Equal(orgDto.Id, assignDto.OrganizationId);
            Assert.Equal(siteDto.Id, assignDto.SiteId);
            Assert.Equal(zoneDto.Id, assignDto.ZoneId);

            // 7. Get Workstation Assignment
            var getAssignRes = await _client.GetAsync($"/api/workstations/{wsId}/assignment");
            Assert.Equal(HttpStatusCode.OK, getAssignRes.StatusCode);
            var fetchedAssign = await getAssignRes.Content.ReadFromJsonAsync<WorkstationAssignmentResponseDto>();
            Assert.NotNull(fetchedAssign);
            Assert.Equal(wsId, fetchedAssign.WorkstationId);
        }

        [Fact]
        public async Task Duplicate_Organization_Code_Should_Return_Conflict()
        {
            var code = $"DUP-ORG-{Guid.NewGuid():N}"[..15];
            var req1 = new CreateOrganizationRequestDto { Name = "Org 1", Code = code };
            var req2 = new CreateOrganizationRequestDto { Name = "Org 2", Code = code };

            var res1 = await _client.PostAsJsonAsync("/api/organizations", req1);
            Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

            var res2 = await _client.PostAsJsonAsync("/api/organizations", req2);
            Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
        }

        [Fact]
        public async Task Duplicate_Site_Code_In_Same_Organization_Should_Return_Conflict()
        {
            var orgReq = new CreateOrganizationRequestDto
            {
                Name = "Org Site Dup Test",
                Code = $"ORG-SD-{Guid.NewGuid():N}"[..15]
            };
            var orgRes = await _client.PostAsJsonAsync("/api/organizations", orgReq);
            var orgDto = await orgRes.Content.ReadFromJsonAsync<OrganizationResponseDto>();

            var siteReq1 = new CreateSiteRequestDto { OrganizationId = orgDto!.Id, Name = "Site 1", Code = "S1" };
            var siteReq2 = new CreateSiteRequestDto { OrganizationId = orgDto.Id, Name = "Site 2", Code = "S1" };

            var res1 = await _client.PostAsJsonAsync("/api/sites", siteReq1);
            Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

            var res2 = await _client.PostAsJsonAsync("/api/sites", siteReq2);
            Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
        }
    }
}

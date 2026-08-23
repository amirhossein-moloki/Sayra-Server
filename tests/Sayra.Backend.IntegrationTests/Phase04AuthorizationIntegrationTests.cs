using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sayra.Backend.Api;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Infrastructure.Persistence;
using Xunit;

namespace Sayra.Backend.IntegrationTests
{
    public class Phase04AuthorizationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _adminClient;
        private readonly HttpClient _unauthClient;

        public Phase04AuthorizationIntegrationTests(WebApplicationFactory<Program> factory)
        {
            EnvLoader.Load();
            _factory = factory;

            TestAdminSeeder.EnsureAdminUserCreatedAsync(factory).GetAwaiter().GetResult();

            _adminClient = factory.CreateClient();
            _adminClient.DefaultRequestHeaders.Add("X-User-Id", TestAdminSeeder.AdminUserId.ToString());
            _adminClient.DefaultRequestHeaders.Add("X-User-Role", "Administrator");

            _unauthClient = factory.CreateClient();
        }

        [Fact]
        public async Task Unauthenticated_API_Calls_Return_401_Unauthorized()
        {
            var response = await _unauthClient.GetAsync("/api/sessions/workstation/" + Guid.NewGuid() + "/active");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            var payResp = await _unauthClient.GetAsync($"/api/payments/{Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.Unauthorized, payResp.StatusCode);
        }

        [Fact]
        public async Task Authenticated_Gamer_Attempting_Admin_Endpoint_Returns_403_Forbidden()
        {
            // Create a valid Gamer record in PostgreSQL first
            var createGamerRes = await _adminClient.PostAsJsonAsync("/api/gamers", new
            {
                username = $"gamer_test_{Guid.NewGuid():N}"[..12],
                email = $"gamer_{Guid.NewGuid():N}@test.dev",
                password = "Password123!"
            });
            Assert.Equal(HttpStatusCode.Created, createGamerRes.StatusCode);
            var gamerDto = await createGamerRes.Content.ReadFromJsonAsync<GamerResponseDto>();
            Assert.NotNull(gamerDto);

            var gamerClient = _factory.CreateClient();
            gamerClient.DefaultRequestHeaders.Add("X-Gamer-Id", gamerDto.Id.ToString());
            gamerClient.DefaultRequestHeaders.Add("X-User-Role", "Gamer");

            var orgReq = new CreateOrganizationRequestDto
            {
                Name = "Forbidden Org",
                Code = $"ORG_FORBID_{Guid.NewGuid():N}"[..12]
            };

            var response = await gamerClient.PostAsJsonAsync("/api/organizations", orgReq);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Gamer_Attempting_To_View_Another_Gamer_Reservation_Returns_403_Forbidden()
        {
            string suffix1 = Guid.NewGuid().ToString("N")[..8];
            string suffix2 = Guid.NewGuid().ToString("N")[..8];

            // 1. Admin creates hierarchy and gamer 1 & gamer 2
            var orgRes = await _adminClient.PostAsJsonAsync("/api/organizations", new { code = $"O_{suffix1}", name = "Org" });
            Assert.Equal(HttpStatusCode.Created, orgRes.StatusCode);
            var orgDto = await orgRes.Content.ReadFromJsonAsync<OrganizationResponseDto>();
            Assert.NotNull(orgDto);

            var siteRes = await _adminClient.PostAsJsonAsync("/api/sites", new { organizationId = orgDto.Id, code = $"S_{suffix1}", name = "Site" });
            Assert.Equal(HttpStatusCode.Created, siteRes.StatusCode);
            var siteDto = await siteRes.Content.ReadFromJsonAsync<SiteResponseDto>();
            Assert.NotNull(siteDto);

            var gamer1Res = await _adminClient.PostAsJsonAsync("/api/gamers", new { username = $"g1_{suffix1}", email = $"g1_{suffix1}@t.com", password = "Password123!" });
            Assert.Equal(HttpStatusCode.Created, gamer1Res.StatusCode);
            var gamer1Dto = await gamer1Res.Content.ReadFromJsonAsync<GamerResponseDto>();
            Assert.NotNull(gamer1Dto);

            var gamer2Res = await _adminClient.PostAsJsonAsync("/api/gamers", new { username = $"g2_{suffix2}", email = $"g2_{suffix2}@t.com", password = "Password123!" });
            Assert.Equal(HttpStatusCode.Created, gamer2Res.StatusCode);
            var gamer2Dto = await gamer2Res.Content.ReadFromJsonAsync<GamerResponseDto>();
            Assert.NotNull(gamer2Dto);

            // 2. Admin creates reservation for Gamer 2
            var resReq = new CreateReservationRequestDto
            {
                GamerId = gamer2Dto.Id,
                SiteId = siteDto.Id,
                StartTimeUtc = DateTime.UtcNow.AddHours(1),
                EndTimeUtc = DateTime.UtcNow.AddHours(2)
            };
            var createRes = await _adminClient.PostAsJsonAsync("/api/reservations", resReq);
            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
            var resDto = await createRes.Content.ReadFromJsonAsync<ReservationResponseDto>();
            Assert.NotNull(resDto);

            // 3. Gamer 1 attempts to access Gamer 2's reservation -> returns 403 Forbidden
            var gamer1Client = _factory.CreateClient();
            gamer1Client.DefaultRequestHeaders.Add("X-Gamer-Id", gamer1Dto.Id.ToString());
            gamer1Client.DefaultRequestHeaders.Add("X-User-Role", "Gamer");

            var getRes = await gamer1Client.GetAsync($"/api/reservations/{resDto.ReservationId}");
            Assert.Equal(HttpStatusCode.Forbidden, getRes.StatusCode);
        }

        [Fact]
        public async Task Operator_Attempting_Cross_Site_Session_Access_Returns_403_Forbidden()
        {
            string suffix = Guid.NewGuid().ToString("N")[..8];

            // 1. Create Org, Site A, Site B
            var orgRes = await _adminClient.PostAsJsonAsync("/api/organizations", new { code = $"O_OP_{suffix}", name = "Org Op" });
            Assert.Equal(HttpStatusCode.Created, orgRes.StatusCode);
            var orgDto = await orgRes.Content.ReadFromJsonAsync<OrganizationResponseDto>();
            Assert.NotNull(orgDto);

            var siteARes = await _adminClient.PostAsJsonAsync("/api/sites", new { organizationId = orgDto.Id, code = $"SA_{suffix}", name = "Site A" });
            Assert.Equal(HttpStatusCode.Created, siteARes.StatusCode);
            var siteADto = await siteARes.Content.ReadFromJsonAsync<SiteResponseDto>();
            Assert.NotNull(siteADto);

            var siteBRes = await _adminClient.PostAsJsonAsync("/api/sites", new { organizationId = orgDto.Id, code = $"SB_{suffix}", name = "Site B" });
            Assert.Equal(HttpStatusCode.Created, siteBRes.StatusCode);
            var siteBDto = await siteBRes.Content.ReadFromJsonAsync<SiteResponseDto>();
            Assert.NotNull(siteBDto);

            // 2. Create Operator user assigned to Site A
            Guid operatorUserId = Guid.NewGuid();
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var opUser = new User
                {
                    UserId = $"USR-OP-{suffix}",
                    Username = $"op_{suffix}",
                    Role = UserRole.Operator,
                    Status = UserAccountState.Active,
                    SiteEntityId = siteADto.Id,
                    OrganizationEntityId = orgDto.Id
                };
                typeof(BaseEntity).GetProperty("Id")?.SetValue(opUser, operatorUserId);
                db.Users.Add(opUser);
                await db.SaveChangesAsync();
            }

            // 3. Create Session on Site B
            var gamerRes = await _adminClient.PostAsJsonAsync("/api/gamers", new { username = $"g_op_{suffix}", email = $"g_op_{suffix}@t.com", password = "Password123!" });
            Assert.Equal(HttpStatusCode.Created, gamerRes.StatusCode);
            var gamerDto = await gamerRes.Content.ReadFromJsonAsync<GamerResponseDto>();
            Assert.NotNull(gamerDto);

            Guid wsId = Guid.NewGuid();
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var ws = new Workstation { PcId = $"PC_SB_{suffix}", SiteId = $"SB_{suffix}", Hostname = "H", MacAddress = $"00:11:22:33:44:{Random.Shared.Next(99):D2}", IpAddress = "127.0.0.1", SiteEntityId = siteBDto.Id, OrganizationEntityId = orgDto.Id };
                typeof(BaseEntity).GetProperty("Id")?.SetValue(ws, wsId);
                db.Workstations.Add(ws);

                var session = new Session { OrganizationId = orgDto.Id, SiteId = siteBDto.Id, WorkstationId = wsId, GamerId = gamerDto.Id, Status = "ACTIVE" };
                db.Sessions.Add(session);
                await db.SaveChangesAsync();

                // 4. Operator assigned to Site A attempts access to Session on Site B -> 403
                var operatorClient = _factory.CreateClient();
                operatorClient.DefaultRequestHeaders.Add("X-User-Id", operatorUserId.ToString());
                operatorClient.DefaultRequestHeaders.Add("X-User-Role", "Operator");

                var getSessRes = await operatorClient.GetAsync($"/api/sessions/{session.Id}");
                Assert.Equal(HttpStatusCode.Forbidden, getSessRes.StatusCode);
            }
        }

        [Fact]
        public async Task Database_Enforces_Unique_Constraints_On_Roles_Permissions_And_Mappings()
        {
            string code = $"ROLE_DUP_{Guid.NewGuid():N}"[..12];

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var role1 = new Role { Code = code, Name = "R1" };
            db.Roles.Add(role1);
            await db.SaveChangesAsync();

            var role2 = new Role { Code = code, Name = "R2" };
            db.Roles.Add(role2);
            await Assert.ThrowsAsync<DbUpdateException>(async () => await db.SaveChangesAsync());
        }

        [Fact]
        public async Task Full_RBAC_Lifecycle_Roles_Permissions_And_Immediate_Revocation()
        {
            string suffix = Guid.NewGuid().ToString("N")[..8];

            // 1. Ensure Permission exists in database
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                if (!await db.Permissions.AnyAsync(p => p.Code == "ManageUsers"))
                {
                    db.Permissions.Add(new Permission { Code = "ManageUsers", Name = "Manage Users", Category = "Admin", Status = "Active" });
                    await db.SaveChangesAsync();
                }
            }

            // 2. Admin creates a custom role and permission assignment
            var createRoleResp = await _adminClient.PostAsJsonAsync("/api/roles", new
            {
                code = $"ROLE_CUSTOM_{suffix}",
                name = $"Custom Role {suffix}",
                description = "Custom test role"
            });
            Assert.Equal(HttpStatusCode.Created, createRoleResp.StatusCode);
            var roleDto = await createRoleResp.Content.ReadFromJsonAsync<RoleResponseDto>();
            Assert.NotNull(roleDto);

            // 3. Assign permission to custom role
            var assignPermResp = await _adminClient.PostAsJsonAsync($"/api/roles/{roleDto.Code}/permissions", new
            {
                permissionCode = "ManageUsers"
            });
            Assert.Equal(HttpStatusCode.OK, assignPermResp.StatusCode);

            // 3. Create a test user in DB
            Guid testUserId = Guid.NewGuid();
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = new User
                {
                    UserId = $"USR_RBAC_{suffix}",
                    Username = $"rbac_usr_{suffix}",
                    Role = UserRole.Operator,
                    Status = UserAccountState.Active
                };
                typeof(BaseEntity).GetProperty("Id")?.SetValue(user, testUserId);
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }

            // 4. Assign custom role to test user
            var assignRoleResp = await _adminClient.PostAsJsonAsync($"/api/users/{testUserId}/roles", new
            {
                roleCode = roleDto.Code
            });
            Assert.Equal(HttpStatusCode.OK, assignRoleResp.StatusCode);

            // Duplicate role assignment should return 409 Conflict
            var dupAssignResp = await _adminClient.PostAsJsonAsync($"/api/users/{testUserId}/roles", new
            {
                roleCode = roleDto.Code
            });
            Assert.Equal(HttpStatusCode.Conflict, dupAssignResp.StatusCode);

            // 5. User accesses protected endpoint -> Success
            var userClient = _factory.CreateClient();
            userClient.DefaultRequestHeaders.Add("X-User-Id", testUserId.ToString());

            var getRolesResp = await userClient.GetAsync("/api/roles");
            Assert.Equal(HttpStatusCode.OK, getRolesResp.StatusCode);

            // 6. Disable the role -> subsequent call to protected endpoint immediately returns 403 Forbidden
            var disableRoleResp = await _adminClient.PostAsJsonAsync($"/api/roles/{roleDto.Code}/disable", new { });
            Assert.Equal(HttpStatusCode.OK, disableRoleResp.StatusCode);

            var getRolesDisabledResp = await userClient.GetAsync("/api/roles");
            Assert.Equal(HttpStatusCode.Forbidden, getRolesDisabledResp.StatusCode);
        }
    }
}

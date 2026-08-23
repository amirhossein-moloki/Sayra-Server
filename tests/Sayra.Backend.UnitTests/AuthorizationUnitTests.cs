using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Exceptions;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class AuthorizationUnitTests
    {
        private readonly Mock<IRepository<AuditEvent>> _auditRepositoryMock;
        private readonly AuthorizationService _authService;

        public AuthorizationUnitTests()
        {
            _auditRepositoryMock = new Mock<IRepository<AuditEvent>>();
            _authService = new AuthorizationService(_auditRepositoryMock.Object);
        }

        [Fact]
        public void Role_Entity_Normalizes_And_Validates_Correctly()
        {
            var role = new Role { Code = " Manager ", Name = " Manager Name " };
            role.NormalizeAndValidate();

            Assert.Equal("Manager", role.Code);
            Assert.Equal("Manager Name", role.Name);
            Assert.True(role.IsSystemRole);

            var invalidRole = new Role { Code = " " };
            Assert.Throws<InvalidDomainException>((Action)(() => invalidRole.NormalizeAndValidate()));
        }

        [Fact]
        public void Permission_Entity_Normalizes_And_Validates_Correctly()
        {
            var perm = new Permission { Code = " StartSession ", Name = " Start Session ", Category = " Session " };
            perm.NormalizeAndValidate();

            Assert.Equal("StartSession", perm.Code);
            Assert.Equal("Start Session", perm.Name);
            Assert.Equal("Session", perm.Category);

            var invalidPerm = new Permission { Code = "" };
            Assert.Throws<InvalidDomainException>((Action)(() => invalidPerm.NormalizeAndValidate()));
        }

        [Fact]
        public async Task Unauthenticated_Principal_Fails_Authorization_With_UNAUTHORIZED()
        {
            var result = await _authService.AuthorizeAsync(UserPrincipal.Anonymous, PermissionCatalog.ViewSessions);

            Assert.False(result.IsAllowed);
            Assert.Equal("UNAUTHORIZED", result.ErrorCode);
        }

        [Fact]
        public async Task Disabled_Account_Fails_Authorization_With_ACCOUNT_DISABLED()
        {
            var principal = new UserPrincipal
            {
                IsAuthenticated = true,
                UserId = Guid.NewGuid(),
                Roles = new List<string> { RoleCatalog.Gamer },
                Permissions = new List<string> { PermissionCatalog.StartSession },
                AccountStatus = UserAccountState.Disabled
            };

            var result = await _authService.AuthorizeAsync(principal, PermissionCatalog.StartSession);

            Assert.False(result.IsAllowed);
            Assert.Equal("ACCOUNT_DISABLED", result.ErrorCode);
        }

        [Fact]
        public async Task Gamer_Accessing_Another_Gamer_Reservation_Is_Denied()
        {
            Guid gamer1Id = Guid.NewGuid();
            Guid gamer2Id = Guid.NewGuid();

            var principalGamer1 = new UserPrincipal
            {
                IsAuthenticated = true,
                GamerId = gamer1Id,
                Roles = new List<string> { RoleCatalog.Gamer },
                Permissions = new List<string> { PermissionCatalog.ViewReservations },
                AccountStatus = UserAccountState.Active
            };

            var reservationOwnedByGamer2 = new Reservation
            {
                GamerId = gamer2Id,
                OrganizationId = Guid.NewGuid(),
                SiteId = Guid.NewGuid()
            };

            var result = await _authService.AuthorizeAsync(principalGamer1, PermissionCatalog.ViewReservations, reservationOwnedByGamer2);

            Assert.False(result.IsAllowed);
            Assert.Equal("CROSS_GAMER_ACCESS_DENIED", result.ErrorCode);
        }

        [Fact]
        public async Task User_With_Resource_Access_Grant_But_Without_Permission_Is_Denied()
        {
            Guid userId = Guid.NewGuid();
            Guid workstationId = Guid.NewGuid();

            var userAccessRepoMock = new Mock<IRepository<UserResourceAccess>>();
            userAccessRepoMock.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<UserResourceAccess, bool>>>(),
                false,
                default))
                .ReturnsAsync(new List<UserResourceAccess>
                {
                    new UserResourceAccess
                    {
                        UserEntityId = userId,
                        ResourceType = "Workstation",
                        ResourceId = workstationId,
                        IsGranted = true,
                        Status = "Active"
                    }
                });

            var authService = new AuthorizationService(_auditRepositoryMock.Object, userAccessRepoMock.Object, null);

            var principalLackingPerm = new UserPrincipal
            {
                IsAuthenticated = true,
                UserId = userId,
                Roles = new List<string> { RoleCatalog.Operator },
                Permissions = new List<string> { PermissionCatalog.ViewWorkstations }, // Lacks ControlWorkstations
                AccountStatus = UserAccountState.Active
            };

            var ws = new Workstation { PcId = "PC-101" };
            typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(ws, workstationId);

            var result = await authService.AuthorizeAsync(principalLackingPerm, PermissionCatalog.ControlWorkstations, ws);

            Assert.False(result.IsAllowed);
            Assert.Equal("PERMISSION_DENIED", result.ErrorCode);
        }

        [Fact]
        public async Task Explicit_UserResourceAccess_Restriction_Is_Enforced()
        {
            Guid userId = Guid.NewGuid();
            Guid workstationId = Guid.NewGuid();

            var userAccessRepoMock = new Mock<IRepository<UserResourceAccess>>();
            userAccessRepoMock.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<UserResourceAccess, bool>>>(),
                false,
                default))
                .ReturnsAsync(new List<UserResourceAccess>
                {
                    new UserResourceAccess
                    {
                        UserEntityId = userId,
                        ResourceType = "Workstation",
                        ResourceId = workstationId,
                        IsGranted = false,
                        Status = "Active"
                    }
                });

            var authServiceWithExplicitAccess = new AuthorizationService(_auditRepositoryMock.Object, userAccessRepoMock.Object, null);

            var principal = new UserPrincipal
            {
                IsAuthenticated = true,
                UserId = userId,
                Roles = new List<string> { RoleCatalog.Operator },
                Permissions = new List<string> { PermissionCatalog.ControlWorkstations },
                AccountStatus = UserAccountState.Active
            };

            var ws = new Workstation { PcId = "PC-100" };
            typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(ws, workstationId);

            var result = await authServiceWithExplicitAccess.AuthorizeAsync(principal, PermissionCatalog.ControlWorkstations, ws);

            Assert.False(result.IsAllowed);
            Assert.Equal("EXPLICIT_RESTRICTION_DENIED", result.ErrorCode);
        }

        [Fact]
        public async Task Gamer_Accessing_Own_Reservation_Is_Allowed()
        {
            Guid gamerId = Guid.NewGuid();

            var principalGamer = new UserPrincipal
            {
                IsAuthenticated = true,
                GamerId = gamerId,
                Roles = new List<string> { RoleCatalog.Gamer },
                Permissions = new List<string> { PermissionCatalog.ViewReservations },
                AccountStatus = UserAccountState.Active
            };

            var reservationOwnedByGamer = new Reservation
            {
                GamerId = gamerId,
                OrganizationId = Guid.NewGuid(),
                SiteId = Guid.NewGuid()
            };

            var result = await _authService.AuthorizeAsync(principalGamer, PermissionCatalog.ViewReservations, reservationOwnedByGamer);

            Assert.True(result.IsAllowed);
        }

        [Fact]
        public async Task Operator_Accessing_Resource_Outside_Assigned_Site_Is_Denied()
        {
            Guid siteA = Guid.NewGuid();
            Guid siteB = Guid.NewGuid();

            var operatorSiteA = new UserPrincipal
            {
                IsAuthenticated = true,
                UserId = Guid.NewGuid(),
                SiteId = siteA,
                Roles = new List<string> { RoleCatalog.Operator },
                Permissions = new List<string> { PermissionCatalog.ViewSessions },
                AccountStatus = UserAccountState.Active
            };

            var sessionSiteB = new Session
            {
                SiteId = siteB,
                OrganizationId = Guid.NewGuid(),
                WorkstationId = Guid.NewGuid(),
                GamerId = Guid.NewGuid()
            };

            var result = await _authService.AuthorizeAsync(operatorSiteA, PermissionCatalog.ViewSessions, sessionSiteB);

            Assert.False(result.IsAllowed);
            Assert.Equal("CROSS_SITE_ACCESS_DENIED", result.ErrorCode);
        }

        [Fact]
        public async Task Administrator_Bypasses_Site_Restriction_And_Succeeds()
        {
            Guid siteA = Guid.NewGuid();
            Guid siteB = Guid.NewGuid();

            var admin = new UserPrincipal
            {
                IsAuthenticated = true,
                UserId = Guid.NewGuid(),
                SiteId = siteA,
                Roles = new List<string> { RoleCatalog.Administrator },
                AccountStatus = UserAccountState.Active
            };

            var sessionSiteB = new Session
            {
                SiteId = siteB,
                OrganizationId = Guid.NewGuid(),
                WorkstationId = Guid.NewGuid(),
                GamerId = Guid.NewGuid()
            };

            var result = await _authService.AuthorizeAsync(admin, PermissionCatalog.ViewSessions, sessionSiteB);

            Assert.True(result.IsAllowed);
        }

        [Fact]
        public async Task Missing_Required_Permission_Is_Denied_With_PERMISSION_DENIED()
        {
            var principal = new UserPrincipal
            {
                IsAuthenticated = true,
                UserId = Guid.NewGuid(),
                Roles = new List<string> { RoleCatalog.Operator },
                Permissions = new List<string> { PermissionCatalog.ViewWorkstations }, // Lacks ManageUsers
                AccountStatus = UserAccountState.Active
            };

            var result = await _authService.AuthorizeAsync(principal, PermissionCatalog.ManageUsers);

            Assert.False(result.IsAllowed);
            Assert.Equal("PERMISSION_DENIED", result.ErrorCode);
        }

        [Fact]
        public void Role_And_Permission_Status_And_Disable_Enable_Methods_Work_Correctly()
        {
            var role = new Role { Code = "TEST_ROLE", Name = "Test Role" };
            Assert.True(role.IsActive);
            Assert.Equal("Active", role.Status);

            role.Disable();
            Assert.False(role.IsActive);
            Assert.Equal("Disabled", role.Status);
            Assert.NotNull(role.UpdatedAt);

            role.Enable();
            Assert.True(role.IsActive);
            Assert.Equal("Active", role.Status);

            var perm = new Permission { Code = "TEST_PERM", Name = "Test Perm" };
            Assert.True(perm.IsActive);
            Assert.Equal("Active", perm.Status);

            perm.Disable();
            Assert.False(perm.IsActive);
            Assert.Equal("Disabled", perm.Status);
            Assert.NotNull(perm.UpdatedAt);

            perm.Enable();
            Assert.True(perm.IsActive);
            Assert.Equal("Active", perm.Status);
        }

        [Fact]
        public async Task CQRS_RbacHandlers_Prevents_Duplicate_Role_And_Permission_Assignment()
        {
            var userRepoMock = new Mock<IRepository<User>>();
            var roleRepoMock = new Mock<IRepository<Role>>();
            var permRepoMock = new Mock<IRepository<Permission>>();
            var userRoleRepoMock = new Mock<IRepository<UserRoleEntity>>();
            var rolePermRepoMock = new Mock<IRepository<RolePermission>>();
            var auditRepoMock = new Mock<IRepository<AuditEvent>>();
            var authServiceMock = new Mock<IAuthorizationService>();
            var uowMock = new Mock<IUnitOfWork>();

            authServiceMock.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), It.IsAny<string>(), It.IsAny<object>(), default))
                .ReturnsAsync(AuthorizationResult.Allowed());

            Guid userId = Guid.NewGuid();
            var user = new User { Username = "testuser" };
            userRepoMock.Setup(r => r.GetByIdAsync(userId, true, default)).ReturnsAsync(user);

            var activeRole = new Role { Code = "OPERATOR", Name = "Operator", Status = "Active" };
            roleRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Role, bool>>>(), false, default))
                .ReturnsAsync(activeRole);

            // User already has this role assigned
            userRoleRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<UserRoleEntity, bool>>>(), false, default))
                .ReturnsAsync(new UserRoleEntity { UserEntityId = userId, RoleId = activeRole.Id });

            var handlers = new RbacHandlers(
                userRepoMock.Object,
                roleRepoMock.Object,
                permRepoMock.Object,
                userRoleRepoMock.Object,
                rolePermRepoMock.Object,
                auditRepoMock.Object,
                authServiceMock.Object,
                uowMock.Object
            );

            var cmd = new AssignRoleToUserCommand { UserEntityId = userId, RoleCode = "OPERATOR" };
            var result = await handlers.HandleAsync(cmd, default);

            Assert.False(result.IsSuccess);
            Assert.Equal("ROLE_ALREADY_ASSIGNED", result.ErrorCode);
        }

        [Fact]
        public async Task CQRS_RbacHandlers_Disables_Assignment_For_Disabled_Role_Or_Permission()
        {
            var userRepoMock = new Mock<IRepository<User>>();
            var roleRepoMock = new Mock<IRepository<Role>>();
            var permRepoMock = new Mock<IRepository<Permission>>();
            var userRoleRepoMock = new Mock<IRepository<UserRoleEntity>>();
            var rolePermRepoMock = new Mock<IRepository<RolePermission>>();
            var auditRepoMock = new Mock<IRepository<AuditEvent>>();
            var authServiceMock = new Mock<IAuthorizationService>();
            var uowMock = new Mock<IUnitOfWork>();

            authServiceMock.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), It.IsAny<string>(), It.IsAny<object>(), default))
                .ReturnsAsync(AuthorizationResult.Allowed());

            Guid userId = Guid.NewGuid();
            var user = new User { Username = "testuser" };
            userRepoMock.Setup(r => r.GetByIdAsync(userId, true, default)).ReturnsAsync(user);

            var disabledRole = new Role { Code = "DISABLED_ROLE", Name = "Disabled Role", Status = "Disabled" };
            roleRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Role, bool>>>(), false, default))
                .ReturnsAsync(disabledRole);

            var handlers = new RbacHandlers(
                userRepoMock.Object,
                roleRepoMock.Object,
                permRepoMock.Object,
                userRoleRepoMock.Object,
                rolePermRepoMock.Object,
                auditRepoMock.Object,
                authServiceMock.Object,
                uowMock.Object
            );

            var cmd = new AssignRoleToUserCommand { UserEntityId = userId, RoleCode = "DISABLED_ROLE" };
            var result = await handlers.HandleAsync(cmd, default);

            Assert.False(result.IsSuccess);
            Assert.Equal("INVALID_ROLE_STATE", result.ErrorCode);
        }
    }
}

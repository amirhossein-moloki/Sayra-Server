using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sayra.Backend.Api.Security;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Communication;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Commands;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Application.Workstations;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Infrastructure.Security;
using Sayra.Backend.Infrastructure.Transport;
using Sayra.Backend.Shared;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class Phase11AuthenticationAndAuthorizationAuditTests
    {
        private const string TestMasterKey = "0123456789ABCDEF0123456789ABCDEF"; // 32 bytes

        private static ApplicationDbContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: $"AuditTestDb_{Guid.NewGuid()}")
                .Options;
            return new ApplicationDbContext(options);
        }

        #region 1. User Authentication Tests

        [Fact]
        public void User_Authentication_Argon2id_Hashing_And_Salt_Uniqueness()
        {
            var options = Options.Create(new SecurityOptions
            {
                PasswordHashAlgorithm = "Argon2id",
                ArgonDegreeOfParallelism = 1,
                ArgonMemorySizeKb = 1024,
                ArgonIterations = 1,
                SaltSize = 16,
                KeySize = 32
            });

            var hasher = new PasswordHasher(options);

            string password = "SecretMasterPassword123!";
            var hashResult1 = hasher.HashPasswordWithDetails(password);
            var hashResult2 = hasher.HashPasswordWithDetails(password);

            // Verify algorithm
            Assert.Equal("Argon2id", hashResult1.Algorithm);

            // Verify salts are unique per invocation
            Assert.NotEqual(hashResult1.Salt, hashResult2.Salt);
            Assert.NotEqual(hashResult1.Hash, hashResult2.Hash);

            // Verify password verification succeeds
            bool isValid = hasher.VerifyPassword(password, hashResult1.Hash, hashResult1.Salt, hashResult1.Algorithm);
            Assert.True(isValid);

            // Verify incorrect password fails
            bool isInvalid = hasher.VerifyPassword("WrongPassword123!", hashResult1.Hash, hashResult1.Salt, hashResult1.Algorithm);
            Assert.False(isInvalid);
        }

        [Fact]
        public void User_Authentication_Exceeds_MaxPasswordLength_Rejected()
        {
            var options = Options.Create(new SecurityOptions
            {
                MaxPasswordLength = 16
            });

            var hasher = new PasswordHasher(options);
            string longPassword = new string('A', 20);

            Assert.Throws<ArgumentException>(() => hasher.HashPassword(longPassword));
            Assert.False(hasher.VerifyPassword(longPassword, "dummyHash", "dummySalt"));
        }

        #endregion

        #region 2. Client Authentication & Handshake Tests

        [Fact]
        public async Task Client_Authentication_Fake_Client_Invalid_HMAC_Rejected()
        {
            var cryptoService = new Infrastructure.Security.CryptographicService();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SAYRA_MASTER_KEY"] = TestMasterKey
            }).Build();

            var serviceScopeFactoryMock = new Mock<IServiceScopeFactory>();
            var logger = NullLogger<ClientAuthenticationService>.Instance;

            var clientAuthService = new ClientAuthenticationService(cryptoService, config, serviceScopeFactoryMock.Object, logger);

            var tcpConnectionMock = new Mock<ITcpConnection>();
            tcpConnectionMock.Setup(c => c.ConnectionId).Returns("CONN-TEST-001");

            string challenge = await clientAuthService.GenerateChallengeAsync(tcpConnectionMock.Object);
            Assert.NotNull(challenge);

            var fakeResponse = new AuthResponseDto
            {
                PcId = "PC-FAKE-001",
                Hmac = Convert.ToBase64String(new byte[32]), // Fake invalid HMAC
                EncryptedSessionKey = Convert.ToBase64String(new byte[48])
            };

            var result = await clientAuthService.ValidateResponseAsync(tcpConnectionMock.Object, fakeResponse);

            Assert.False(result.IsSuccess);
            Assert.Equal("AUTH_FAILED", result.ErrorCode);
            Assert.Equal(ConnectionLifecycleState.Disconnected, result.NewState);
        }

        #endregion

        #region 3. Token & Session Security Tests

        [Fact]
        public async Task Token_And_Session_Security_Revoked_Token_Is_Denied()
        {
            var sessionRepoMock = new Mock<IRepository<AuthenticationSession>>();
            var redisServiceMock = new Mock<IRedisService>();
            var unitOfWorkMock = new Mock<IUnitOfWork>();
            var logger = NullLogger<AuthenticationSessionService>.Instance;

            string token = "test_token_12345";
            redisServiceMock.Setup(r => r.GetStringAsync($"sayra:auth:revoked:{token}", It.IsAny<CancellationToken>()))
                .ReturnsAsync("1");

            var authSessionService = new AuthenticationSessionService(sessionRepoMock.Object, redisServiceMock.Object, unitOfWorkMock.Object, logger);

            bool isValid = await authSessionService.ValidateSessionAsync(token);

            Assert.False(isValid);
        }

        [Fact]
        public async Task Token_And_Session_Security_Expired_Token_Is_Denied()
        {
            var sessionRepoMock = new Mock<IRepository<AuthenticationSession>>();
            var redisServiceMock = new Mock<IRedisService>();
            var unitOfWorkMock = new Mock<IUnitOfWork>();
            var logger = NullLogger<AuthenticationSessionService>.Instance;

            string token = "test_expired_token";
            redisServiceMock.Setup(r => r.GetStringAsync($"sayra:auth:revoked:{token}", It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);

            var expiredSession = new AuthenticationSession
            {
                SessionToken = token,
                Status = AuthenticationSession.StatusActive,
                ExpiresAt = DateTime.UtcNow.AddHours(-1) // Expired 1 hr ago
            };

            redisServiceMock.Setup(r => r.GetAsync<AuthenticationSession>($"sayra:auth:session:{token}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(expiredSession);

            var authSessionService = new AuthenticationSessionService(sessionRepoMock.Object, redisServiceMock.Object, unitOfWorkMock.Object, logger);

            bool isValid = await authSessionService.ValidateSessionAsync(token);

            Assert.False(isValid);
        }

        #endregion

        #region 4. Authorization & Privilege Escalation Tests

        [Fact]
        public async Task Horizontal_Privilege_Escalation_GamerA_Cannot_Access_GamerB_Data()
        {
            var auditRepoMock = new Mock<IRepository<AuditEvent>>();
            var secEventServiceMock = new Mock<ISecurityEventService>();

            var authService = new AuthorizationService(auditRepoMock.Object, securityEventService: secEventServiceMock.Object);

            Guid gamerAId = Guid.NewGuid();
            Guid gamerBId = Guid.NewGuid();

            var gamerAPrincipal = new UserPrincipal
            {
                IsAuthenticated = true,
                UserId = gamerAId,
                GamerId = gamerAId,
                Roles = new List<string> { RoleCatalog.Gamer },
                Permissions = new List<string> { PermissionCatalog.ViewSessions }
            };

            var gamerBSession = new Session
            {
                GamerId = gamerBId, // Belonging to Gamer B
                SiteId = Guid.NewGuid(),
                OrganizationId = Guid.NewGuid()
            };

            var authResult = await authService.AuthorizeAsync(gamerAPrincipal, PermissionCatalog.ViewSessions, gamerBSession);

            Assert.False(authResult.IsAllowed);
            Assert.Equal("CROSS_GAMER_ACCESS_DENIED", authResult.ErrorCode);
        }

        [Fact]
        public async Task Vertical_Privilege_Escalation_Gamer_Cannot_Execute_Admin_Operation()
        {
            var auditRepoMock = new Mock<IRepository<AuditEvent>>();
            var authService = new AuthorizationService(auditRepoMock.Object);

            var gamerPrincipal = new UserPrincipal
            {
                IsAuthenticated = true,
                UserId = Guid.NewGuid(),
                GamerId = Guid.NewGuid(),
                Roles = new List<string> { RoleCatalog.Gamer },
                Permissions = new List<string> { PermissionCatalog.StartSession, PermissionCatalog.ViewSessions }
            };

            // Gamer attempting Admin Manage Workstations operation
            var authResult = await authService.AuthorizeAsync(gamerPrincipal, PermissionCatalog.ManageWorkstations);

            Assert.False(authResult.IsAllowed);
            Assert.Equal("PERMISSION_DENIED", authResult.ErrorCode);
        }

        [Fact]
        public async Task Client_Privilege_Escalation_Cross_Workstation_Command_Forgery_Denied()
        {
            var scopeFactoryMock = new Mock<IServiceScopeFactory>();
            var scopeMock = new Mock<IServiceScope>();
            var serviceProviderMock = new Mock<IServiceProvider>();
            var connectionRegistryMock = new Mock<ITcpConnectionRegistry>();
            var secureMessageServiceMock = new Mock<ISecureMessageService>();
            var redisServiceMock = new Mock<IRedisService>();
            var logger = NullLogger<RemoteCommandManager>.Instance;
            using var dbContext = CreateInMemoryDbContext();

            scopeFactoryMock.Setup(s => s.CreateScope()).Returns(scopeMock.Object);
            scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
            serviceProviderMock.Setup(s => s.GetService(typeof(ApplicationDbContext))).Returns(dbContext);

            var commandManager = new RemoteCommandManager(scopeFactoryMock.Object, connectionRegistryMock.Object, secureMessageServiceMock.Object, redisServiceMock.Object, logger);

            var remoteCmd = RemoteCommand.Create(
                "CMD-TARGET-01",
                "LOCK_WORKSTATION",
                Guid.NewGuid(),
                "PC-01", // Target PC-01
                "ADMIN",
                null,
                TimeSpan.FromMinutes(5));

            await dbContext.RemoteCommands.AddAsync(remoteCmd);
            await dbContext.SaveChangesAsync();

            // Client on PC-02 attempts to send ACK for PC-01's command
            var ackResult = await commandManager.ProcessCommandAckAsync("CMD-TARGET-01", "PC-02", "ACKNOWLEDGED", null);

            Assert.False(ackResult.IsSuccess);
            Assert.Equal("CROSS_WORKSTATION_FORGERY", ackResult.ErrorCode);
        }

        #endregion

        #region 5. Authorization Failure Handling Tests

        [Fact]
        public async Task Authorization_Failure_Handling_Returns_Sanitized_Forbidden_Payload()
        {
            var authServiceMock = new Mock<IAuthorizationService>();
            authServiceMock.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), "manage:users", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(AuthorizationResult.Denied("Permission missing.", "PERMISSION_DENIED"));

            var filter = new PermissionAuthorizationFilter("manage:users", authServiceMock.Object);

            var actionContext = new ActionContext
            {
                HttpContext = new DefaultHttpContext(),
                RouteData = new RouteData(),
                ActionDescriptor = new ActionDescriptor()
            };

            var filterContext = new ActionExecutingContext(
                actionContext,
                new List<IFilterMetadata>(),
                new Dictionary<string, object?>(),
                new object()
            );

            ActionExecutionDelegate nextDelegate = () => Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));

            await filter.OnActionExecutionAsync(filterContext, nextDelegate);

            Assert.NotNull(filterContext.Result);
            var objectResult = Assert.IsType<ObjectResult>(filterContext.Result);
            Assert.Equal(403, objectResult.StatusCode);

            // Verify payload structure contains only sanitized codes, no SQL or stack trace
            var json = ProtocolSerialization.Serialize(objectResult.Value);
            Assert.Contains("PERMISSION_DENIED", json);
            Assert.DoesNotContain("Exception", json);
            Assert.DoesNotContain("SELECT", json);
        }

        #endregion

        #region 6. Audit Logging Verification Tests

        [Fact]
        public async Task Security_Audit_Logging_Emits_AuditEvent_And_SecurityEvent_On_Authorization_Denial()
        {
            var auditRepoMock = new Mock<IRepository<AuditEvent>>();
            var secEventServiceMock = new Mock<ISecurityEventService>();

            var authService = new AuthorizationService(auditRepoMock.Object, securityEventService: secEventServiceMock.Object);

            var principal = new UserPrincipal
            {
                IsAuthenticated = true,
                UserId = Guid.NewGuid(),
                Roles = new List<string> { RoleCatalog.Gamer },
                Permissions = new List<string>()
            };

            await authService.AuthorizeAsync(principal, PermissionCatalog.ManageRoles);

            // Verify audit event persisted
            auditRepoMock.Verify(a => a.AddAsync(It.Is<AuditEvent>(e =>
                e.EventType == "AUTHORIZATION_DENIED" &&
                e.Payload.Contains("ManageRoles")
            ), It.IsAny<CancellationToken>()), Times.Once);

            // Verify security event recorded
            secEventServiceMock.Verify(s => s.RecordSecurityEventAsync(
                "AUTHORIZATION_DENIED",
                principal.UserId,
                "User",
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                "Permission",
                It.IsAny<Guid?>(),
                PermissionCatalog.ManageRoles,
                "DENIED",
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()
            ), Times.Once);
        }

        #endregion
    }
}

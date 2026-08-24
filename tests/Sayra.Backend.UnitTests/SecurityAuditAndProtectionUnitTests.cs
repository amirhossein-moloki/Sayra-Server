using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Infrastructure.Security;

namespace Sayra.Backend.UnitTests
{
    public class SecurityAuditAndProtectionUnitTests
    {
        private readonly Mock<IRepository<SecurityEvent>> _securityEventRepoMock = new();
        private readonly Mock<IRepository<LoginAttempt>> _loginAttemptRepoMock = new();
        private readonly Mock<IRepository<User>> _userRepoMock = new();
        private readonly Mock<IRepository<Gamer>> _gamerRepoMock = new();
        private readonly Mock<IRepository<GamerCredential>> _gamerCredRepoMock = new();
        private readonly Mock<IRedisService> _redisServiceMock = new();
        private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();

        [Fact]
        public async Task SecurityEvent_Sanitizes_Sensitive_Information()
        {
            SecurityEvent? capturedEvent = null;

            _securityEventRepoMock
                .Setup(r => r.AddAsync(It.IsAny<SecurityEvent>(), It.IsAny<CancellationToken>()))
                .Callback<SecurityEvent, CancellationToken>((ev, _) => capturedEvent = ev)
                .Returns(Task.CompletedTask);

            var secLoggerMock = new Mock<ILogger<SecurityEventService>>();
            var service = new SecurityEventService(_securityEventRepoMock.Object, _unitOfWorkMock.Object, secLoggerMock.Object);

            string unsanitizedReason = "Invalid credentials. Password=SecretPassword123! Token=secret_token_xyz";

            await service.RecordSecurityEventAsync(
                eventType: "LOGIN_FAILED",
                actorId: Guid.NewGuid(),
                actorType: "User",
                deviceId: "PC-01",
                organizationId: Guid.NewGuid(),
                siteId: Guid.NewGuid(),
                resourceType: "Account",
                resourceId: Guid.NewGuid(),
                action: "LOGIN",
                result: "FAILED",
                failureReason: unsanitizedReason,
                cancellationToken: CancellationToken.None);

            Assert.NotNull(capturedEvent);
            Assert.Equal("LOGIN_FAILED", capturedEvent.EventType);
            Assert.DoesNotContain("SecretPassword123!", capturedEvent.FailureReason);
            Assert.DoesNotContain("secret_token_xyz", capturedEvent.FailureReason);
            Assert.Contains("Password=[REDACTED]", capturedEvent.FailureReason);
            Assert.Contains("Token=[REDACTED]", capturedEvent.FailureReason);
        }

        [Fact]
        public async Task LoginProtection_Triggers_Lockout_After_Max_Failed_Attempts()
        {
            var secEventServiceMock = new Mock<ISecurityEventService>();

            _redisServiceMock
                .Setup(r => r.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(5);

            _loginAttemptRepoMock
                .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<LoginAttempt, bool>>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((LoginAttempt?)null);

            var loginLoggerMock = new Mock<ILogger<LoginProtectionService>>();
            var service = new LoginProtectionService(
                _redisServiceMock.Object,
                _loginAttemptRepoMock.Object,
                _userRepoMock.Object,
                _gamerRepoMock.Object,
                _gamerCredRepoMock.Object,
                secEventServiceMock.Object,
                _unitOfWorkMock.Object,
                loginLoggerMock.Object);

            await service.RecordFailedAttemptAsync("testuser", userId: Guid.NewGuid(), failureReason: "Wrong password");

            secEventServiceMock.Verify(s => s.RecordSecurityEventAsync(
                It.Is<string>(e => e == "ACCOUNT_LOCKED"),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.Is<string>(r => r == "LOCKED"),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UnlockAsync_Resets_Attempts_And_Emits_AccountUnlocked_Event()
        {
            var secEventServiceMock = new Mock<ISecurityEventService>();

            var loginLoggerMock = new Mock<ILogger<LoginProtectionService>>();
            var service = new LoginProtectionService(
                _redisServiceMock.Object,
                _loginAttemptRepoMock.Object,
                _userRepoMock.Object,
                _gamerRepoMock.Object,
                _gamerCredRepoMock.Object,
                secEventServiceMock.Object,
                _unitOfWorkMock.Object,
                loginLoggerMock.Object);

            await service.UnlockAsync("testuser");

            _redisServiceMock.Verify(r => r.RemoveAsync("sayra:login:attempts:testuser", It.IsAny<CancellationToken>()), Times.Once);
            _redisServiceMock.Verify(r => r.RemoveAsync("sayra:login:lockout:testuser", It.IsAny<CancellationToken>()), Times.Once);

            secEventServiceMock.Verify(s => s.RecordSecurityEventAsync(
                It.Is<string>(e => e == "ACCOUNT_UNLOCKED"),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.Is<string>(r => r == "SUCCESS"),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AccessAuditService_Records_Authorization_Granted_And_Denied()
        {
            var secEventServiceMock = new Mock<ISecurityEventService>();
            var auditService = new AccessAuditService(secEventServiceMock.Object);

            var principal = new UserPrincipal
            {
                UserId = Guid.NewGuid(),
                Username = "testadmin",
                Roles = new List<string> { "Administrator" },
                AccountStatus = UserAccountState.Active
            };

            // 1. Granted
            await auditService.RecordAuthorizationGrantedAsync(principal, "StartSession");

            secEventServiceMock.Verify(s => s.RecordSecurityEventAsync(
                It.Is<string>(e => e == "AUTHORIZATION_GRANTED"),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.Is<string>(a => a == "StartSession"),
                It.Is<string>(r => r == "GRANTED"),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()), Times.Once);

            // 2. Denied
            await auditService.RecordAuthorizationDeniedAsync(principal, "StartSession", "Permission denied");

            secEventServiceMock.Verify(s => s.RecordSecurityEventAsync(
                It.Is<string>(e => e == "AUTHORIZATION_DENIED"),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.Is<string>(a => a == "StartSession"),
                It.Is<string>(r => r == "DENIED"),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AccessAuditService_Records_Device_Events()
        {
            var secEventServiceMock = new Mock<ISecurityEventService>();
            var auditService = new AccessAuditService(secEventServiceMock.Object);

            // 1. Device Handshake Failed
            await auditService.RecordDeviceHandshakeFailedAsync("PC-01", "HMAC mismatch");

            secEventServiceMock.Verify(s => s.RecordSecurityEventAsync(
                It.Is<string>(e => e == "DEVICE_AUTHENTICATION_FAILED"),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.Is<string?>(d => d == "PC-01"),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.Is<string>(r => r == "FAILED"),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()), Times.Once);

            // 2. Device Registered
            await auditService.RecordDeviceRegisteredAsync("PC-01");

            secEventServiceMock.Verify(s => s.RecordSecurityEventAsync(
                It.Is<string>(e => e == "DEVICE_REGISTERED"),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.Is<string?>(d => d == "PC-01"),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.Is<string>(r => r == "SUCCESS"),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}

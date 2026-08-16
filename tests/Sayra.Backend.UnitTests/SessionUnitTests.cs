using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Sessions;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class SessionUnitTests
    {
        [Fact]
        public void Session_State_Machine_Valid_Transitions_Should_Succeed()
        {
            var session = CreateValidSession();
            Assert.Equal("IDLE", session.Status);

            // IDLE -> STARTING
            session.TransitionTo("STARTING");
            Assert.Equal("STARTING", session.Status);

            // STARTING -> ACTIVE
            session.TransitionTo("ACTIVE");
            Assert.Equal("ACTIVE", session.Status);
            Assert.True(session.IsActive());

            // ACTIVE -> PAUSED
            session.TransitionTo("PAUSED");
            Assert.Equal("PAUSED", session.Status);
            Assert.NotNull(session.PausedAt);
            Assert.True(session.IsActive());

            // PAUSED -> ACTIVE
            session.TransitionTo("ACTIVE");
            Assert.Equal("ACTIVE", session.Status);
            Assert.Null(session.PausedAt);

            // ACTIVE -> ENDING
            session.TransitionTo("ENDING");
            Assert.Equal("ENDING", session.Status);
            Assert.True(session.IsActive());

            // ENDING -> ENDED
            session.TransitionTo("ENDED");
            Assert.Equal("ENDED", session.Status);
            Assert.NotNull(session.EndedAt);
            Assert.False(session.IsActive());
        }

        [Theory]
        [InlineData("ENDED")]
        [InlineData("EXPIRED")]
        [InlineData("CANCELLED")]
        [InlineData("TERMINATED")]
        public void Session_Terminal_States_Cannot_Transition_To_Active(string terminalStatus)
        {
            var session = CreateValidSession();
            session.TransitionTo("STARTING");
            session.TransitionTo("ACTIVE");
            session.TransitionTo(terminalStatus);

            var ex = Assert.Throws<InvalidDomainException>(() => session.TransitionTo("ACTIVE"));
            Assert.Equal("INVALID_TRANSITION", ex.ErrorCode);
        }

        [Fact]
        public void Session_NormalizeAndValidate_Empty_GamerId_Should_Throw()
        {
            var session = CreateValidSession();
            session.GamerId = Guid.Empty;

            var ex = Assert.Throws<InvalidDomainException>(() => session.NormalizeAndValidate());
            Assert.Equal("INVALID_GAMER_ID", ex.ErrorCode);
        }

        [Fact]
        public void Session_NormalizeAndValidate_Empty_WorkstationId_Should_Throw()
        {
            var session = CreateValidSession();
            session.WorkstationId = Guid.Empty;

            var ex = Assert.Throws<InvalidDomainException>(() => session.NormalizeAndValidate());
            Assert.Equal("INVALID_WORKSTATION_ID", ex.ErrorCode);
        }

        [Fact]
        public void Session_TransitionTo_Same_Status_Is_Idempotent()
        {
            var session = CreateValidSession();
            session.TransitionTo("STARTING");
            session.TransitionTo("STARTING"); // Duplicate call

            Assert.Equal("STARTING", session.Status);
        }

        [Fact]
        public async Task SessionService_Active_Session_Conflict_Should_Fail()
        {
            var gamer = new Gamer { Status = "Active", Username = "gamer1", Email = "gamer1@sayra.dev" };
            var site = new Site { Code = "SITE-01", Name = "Main Site", Status = "Active" };
            var org = new Organization { Code = "ORG-01", Name = "Org 1", Status = "Active" };
            var ws = new Workstation { PcId = "PC-01", OrganizationEntityId = org.Id, SiteEntityId = site.Id, IsDisabled = false, IsDeactivated = false };

            var mockGamerRepo = new Mock<IRepository<Gamer>>();
            var mockWsRepo = new Mock<IRepository<Workstation>>();
            var mockResRepo = new Mock<IRepository<Reservation>>();
            var mockSessionRepo = new Mock<IRepository<Session>>();

            mockGamerRepo.Setup(r => r.GetByIdAsync(gamer.Id, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(gamer);

            mockWsRepo.Setup(r => r.GetByIdAsync(ws.Id, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ws);

            var existingSession = new Session
            {
                OrganizationId = org.Id,
                SiteId = site.Id,
                WorkstationId = ws.Id,
                GamerId = gamer.Id,
                Status = "ACTIVE"
            };

            mockSessionRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Session, bool>>>(), false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingSession);

            var service = new SessionStateTransitionService(
                mockGamerRepo.Object,
                mockWsRepo.Object,
                mockResRepo.Object,
                mockSessionRepo.Object);

            var result = await service.ValidateNewSessionAsync(gamer.Id, ws.Id, null);

            Assert.False(result.IsSuccess);
            Assert.Equal("WORKSTATION_HAS_ACTIVE_SESSION", result.ErrorCode);
        }

        [Fact]
        public async Task SessionService_Disabled_Gamer_Should_Fail()
        {
            var gamer = new Gamer { Status = "Disabled", Username = "gamer1", Email = "gamer1@sayra.dev" };
            var mockGamerRepo = new Mock<IRepository<Gamer>>();
            var mockWsRepo = new Mock<IRepository<Workstation>>();
            var mockResRepo = new Mock<IRepository<Reservation>>();
            var mockSessionRepo = new Mock<IRepository<Session>>();

            mockGamerRepo.Setup(r => r.GetByIdAsync(gamer.Id, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(gamer);

            var service = new SessionStateTransitionService(
                mockGamerRepo.Object,
                mockWsRepo.Object,
                mockResRepo.Object,
                mockSessionRepo.Object);

            var result = await service.ValidateNewSessionAsync(gamer.Id, Guid.NewGuid(), null);

            Assert.False(result.IsSuccess);
            Assert.Equal("GAMER_DISABLED", result.ErrorCode);
        }

        private static Session CreateValidSession()
        {
            return new Session
            {
                OrganizationId = Guid.NewGuid(),
                SiteId = Guid.NewGuid(),
                WorkstationId = Guid.NewGuid(),
                GamerId = Guid.NewGuid(),
                Status = "IDLE",
                StartedAt = DateTime.UtcNow
            };
        }
    }
}

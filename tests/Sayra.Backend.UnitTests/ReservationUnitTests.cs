using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Reservations;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class ReservationUnitTests
    {
        [Fact]
        public void Reservation_State_Machine_Valid_Transitions_Should_Succeed()
        {
            var reservation = CreateValidReservation();
            Assert.Equal("PENDING", reservation.Status);

            // PENDING -> CONFIRMED
            reservation.TransitionTo("CONFIRMED");
            Assert.Equal("CONFIRMED", reservation.Status);

            // CONFIRMED -> ACTIVE
            reservation.TransitionTo("ACTIVE");
            Assert.Equal("ACTIVE", reservation.Status);

            // ACTIVE -> COMPLETED
            reservation.TransitionTo("COMPLETED");
            Assert.Equal("COMPLETED", reservation.Status);
        }

        [Theory]
        [InlineData("CANCELLED")]
        [InlineData("EXPIRED")]
        [InlineData("NO_SHOW")]
        public void Reservation_Pending_Can_Transition_To_Terminal_Or_Cancelled_States(string targetStatus)
        {
            var reservation = CreateValidReservation();
            reservation.TransitionTo(targetStatus);
            Assert.Equal(targetStatus, reservation.Status);
        }

        [Fact]
        public void Reservation_Completed_Cannot_Transition_To_Active()
        {
            var reservation = CreateValidReservation();
            reservation.TransitionTo("CONFIRMED");
            reservation.TransitionTo("ACTIVE");
            reservation.TransitionTo("COMPLETED");

            var ex = Assert.Throws<InvalidDomainException>(() => reservation.TransitionTo("ACTIVE"));
            Assert.Equal("INVALID_TRANSITION", ex.ErrorCode);
        }

        [Fact]
        public void Reservation_Cancelled_Cannot_Transition_To_Active()
        {
            var reservation = CreateValidReservation();
            reservation.TransitionTo("CANCELLED");

            var ex = Assert.Throws<InvalidDomainException>(() => reservation.TransitionTo("ACTIVE"));
            Assert.Equal("INVALID_TRANSITION", ex.ErrorCode);
        }

        [Fact]
        public void Reservation_Expired_Cannot_Transition_To_Active()
        {
            var reservation = CreateValidReservation();
            reservation.TransitionTo("EXPIRED");

            var ex = Assert.Throws<InvalidDomainException>(() => reservation.TransitionTo("ACTIVE"));
            Assert.Equal("INVALID_TRANSITION", ex.ErrorCode);
        }

        [Fact]
        public void Reservation_NormalizeAndValidate_InvalidTimeRange_Should_Throw()
        {
            var reservation = CreateValidReservation();
            reservation.StartTimeUtc = DateTime.UtcNow.AddHours(2);
            reservation.EndTimeUtc = DateTime.UtcNow.AddHours(1); // Invalid: End before Start

            var ex = Assert.Throws<InvalidDomainException>(() => reservation.NormalizeAndValidate());
            Assert.Equal("INVALID_TIME_RANGE", ex.ErrorCode);
        }

        [Fact]
        public void Reservation_NormalizeAndValidate_NegativeReservedAmount_Should_Throw()
        {
            var reservation = CreateValidReservation();
            reservation.ReservedAmount = -10.00m;

            var ex = Assert.Throws<InvalidDomainException>(() => reservation.NormalizeAndValidate());
            Assert.Equal("INVALID_RESERVED_AMOUNT", ex.ErrorCode);
        }

        [Fact]
        public async Task ValidationService_Overlapping_Workstation_Reservation_Should_Return_Conflict()
        {
            // Arrange
            var gamer = new Gamer { Status = "Active", Username = "gamer1", Email = "gamer1@sayra.dev" };
            var site = new Site { Code = "SITE-01", Name = "Main Site", Status = "Active" };
            var org = new Organization { Code = "ORG-01", Name = "Org 1", Status = "Active" };
            site.OrganizationId = org.Id;
            var ws = new Workstation { PcId = "PC-01", SiteEntityId = site.Id, IsDisabled = false, IsDeactivated = false };

            var mockReservationRepo = new Mock<IRepository<Reservation>>();
            var mockGamerRepo = new Mock<IRepository<Gamer>>();
            var mockSiteRepo = new Mock<IRepository<Site>>();
            var mockOrgRepo = new Mock<IRepository<Organization>>();
            var mockWsRepo = new Mock<IRepository<Workstation>>();
            var mockZoneRepo = new Mock<IRepository<Zone>>();

            mockGamerRepo.Setup(r => r.GetByIdAsync(gamer.Id, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(gamer);

            mockSiteRepo.Setup(r => r.GetByIdAsync(site.Id, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(site);

            mockOrgRepo.Setup(r => r.GetByIdAsync(org.Id, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(org);

            mockWsRepo.Setup(r => r.GetByIdAsync(ws.Id, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ws);

            var now = DateTime.UtcNow;
            var existingReservation = new Reservation
            {
                GamerId = gamer.Id,
                SiteId = site.Id,
                OrganizationId = org.Id,
                WorkstationId = ws.Id,
                StartTimeUtc = now.AddHours(1),
                EndTimeUtc = now.AddHours(3),
                Status = "CONFIRMED"
            };

            mockReservationRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Reservation, bool>>>(), false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Reservation> { existingReservation });

            var service = new ReservationValidationService(
                mockReservationRepo.Object,
                mockGamerRepo.Object,
                mockSiteRepo.Object,
                mockOrgRepo.Object,
                mockWsRepo.Object,
                mockZoneRepo.Object);

            // Act - attempt to reserve overlapping range: 2 to 4
            var result = await service.ValidateNewReservationEntitiesAsync(
                gamer.Id,
                site.Id,
                ws.Id,
                null,
                now.AddHours(2),
                now.AddHours(4));

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("RESERVATION_CONFLICT", result.ErrorCode);
        }

        [Fact]
        public async Task ValidationService_NonExistent_Gamer_Should_Return_Error()
        {
            var mockReservationRepo = new Mock<IRepository<Reservation>>();
            var mockGamerRepo = new Mock<IRepository<Gamer>>();
            var mockSiteRepo = new Mock<IRepository<Site>>();
            var mockOrgRepo = new Mock<IRepository<Organization>>();
            var mockWsRepo = new Mock<IRepository<Workstation>>();
            var mockZoneRepo = new Mock<IRepository<Zone>>();

            mockGamerRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Gamer?)null);

            var service = new ReservationValidationService(
                mockReservationRepo.Object,
                mockGamerRepo.Object,
                mockSiteRepo.Object,
                mockOrgRepo.Object,
                mockWsRepo.Object,
                mockZoneRepo.Object);

            var result = await service.ValidateNewReservationEntitiesAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                null,
                DateTime.UtcNow.AddHours(1),
                DateTime.UtcNow.AddHours(2));

            Assert.False(result.IsSuccess);
            Assert.Equal("GAMER_NOT_FOUND", result.ErrorCode);
        }

        private static Reservation CreateValidReservation()
        {
            return new Reservation
            {
                OrganizationId = Guid.NewGuid(),
                SiteId = Guid.NewGuid(),
                GamerId = Guid.NewGuid(),
                StartTimeUtc = DateTime.UtcNow.AddHours(1),
                EndTimeUtc = DateTime.UtcNow.AddHours(3),
                Status = "PENDING",
                ReservedAmount = 15.00m
            };
        }
    }
}

using System;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Events;
using Sayra.Backend.Domain.Exceptions;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class RemoteCommandDomainTests
    {
        [Fact]
        public void Create_ValidParameters_ShouldInstantiateRemoteCommand_AndEmitCreatedEvent()
        {
            // Arrange
            string commandId = "CMD-1001";
            string commandType = "LOCK_WORKSTATION";
            Guid workstationId = Guid.NewGuid();
            string pcId = "PC-FRONT-01";
            string requestedBy = "AdminUser";

            // Act
            var command = RemoteCommand.Create(
                commandId,
                commandType,
                workstationId,
                pcId,
                requestedBy,
                payload: "{\"reason\":\"Maintenance\"}",
                ttl: TimeSpan.FromMinutes(10),
                priority: 1,
                correlationId: "CORR-123",
                isIdempotent: true);

            // Assert
            Assert.NotNull(command);
            Assert.Equal("CMD-1001", command.CommandId);
            Assert.Equal("LOCK_WORKSTATION", command.CommandType);
            Assert.Equal(workstationId, command.TargetWorkstationId);
            Assert.Equal("PC-FRONT-01", command.TargetPcId);
            Assert.Equal("AdminUser", command.RequestedBy);
            Assert.Equal("CREATED", command.Status);
            Assert.False(command.IsTerminal);
            Assert.Single(command.DomainEvents);
            Assert.IsType<RemoteCommandCreatedEvent>(System.Linq.Enumerable.First(command.DomainEvents));
        }

        [Theory]
        [InlineData("", "LOCK_WORKSTATION", "PC-01", "Admin", "INVALID_COMMAND_ID")]
        [InlineData("CMD-1", "", "PC-01", "Admin", "INVALID_COMMAND_TYPE")]
        [InlineData("CMD-1", "LOCK_WORKSTATION", "", "Admin", "INVALID_TARGET_PC_ID")]
        [InlineData("CMD-1", "LOCK_WORKSTATION", "PC-01", "", "INVALID_REQUESTED_BY")]
        public void Create_InvalidParameters_ShouldThrowInvalidDomainException(
            string commandId, string commandType, string pcId, string requestedBy, string expectedErrorCode)
        {
            // Act & Assert
            var ex = Assert.Throws<InvalidDomainException>(() =>
                RemoteCommand.Create(commandId, commandType, Guid.NewGuid(), pcId, requestedBy));

            Assert.Equal(expectedErrorCode, ex.ErrorCode);
        }

        [Fact]
        public void TransitionTo_ValidLifecycleFlow_ShouldUpdateStatusAndTimestamps()
        {
            // Arrange
            var command = RemoteCommand.Create("CMD-2001", "UNLOCK_WORKSTATION", Guid.NewGuid(), "PC-02", "Operator");

            // CREATED -> SENDING
            command.TransitionTo("SENDING");
            Assert.Equal("SENDING", command.Status);

            // SENDING -> DELIVERED
            command.TransitionTo("DELIVERED");
            Assert.Equal("DELIVERED", command.Status);
            Assert.NotNull(command.DeliveredAt);

            // DELIVERED -> ACKNOWLEDGED
            command.TransitionTo("ACKNOWLEDGED");
            Assert.Equal("ACKNOWLEDGED", command.Status);
            Assert.NotNull(command.AcknowledgedAt);

            // ACKNOWLEDGED -> EXECUTING
            command.TransitionTo("EXECUTING");
            Assert.Equal("EXECUTING", command.Status);
            Assert.NotNull(command.ExecutingAt);

            // EXECUTING -> SUCCEEDED
            command.TransitionTo("SUCCEEDED", resultPayload: "{\"unlocked\":true}");
            Assert.Equal("SUCCEEDED", command.Status);
            Assert.True(command.IsTerminal);
            Assert.NotNull(command.CompletedAt);
            Assert.Equal("{\"unlocked\":true}", command.ResultPayload);
        }

        [Fact]
        public void TransitionTo_InvalidTransitionFromTerminalState_ShouldThrowInvalidDomainException()
        {
            // Arrange
            var command = RemoteCommand.Create("CMD-3001", "RESTART_WORKSTATION", Guid.NewGuid(), "PC-03", "Admin");
            command.TransitionTo("SENDING");
            command.TransitionTo("DELIVERY_TIMEOUT");
            Assert.True(command.IsTerminal);

            // Act & Assert
            var ex = Assert.Throws<InvalidDomainException>(() => command.TransitionTo("SUCCEEDED"));
            Assert.Equal("INVALID_TRANSITION", ex.ErrorCode);
        }

        [Fact]
        public void TransitionTo_InvalidStateJump_ShouldThrowInvalidDomainException()
        {
            // Arrange
            var command = RemoteCommand.Create("CMD-4001", "SHUTDOWN_WORKSTATION", Guid.NewGuid(), "PC-04", "Admin");

            // Act & Assert (CREATED directly to EXECUTING is invalid)
            var ex = Assert.Throws<InvalidDomainException>(() => command.TransitionTo("EXECUTING"));
            Assert.Equal("INVALID_TRANSITION", ex.ErrorCode);
        }
    }
}

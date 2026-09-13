using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.OfflineQueue;
using Sayra.Backend.Application.Sessions;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Shared;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class BusinessReconciliationIntegrationTests
    {
        private class TestWorkstation : Workstation
        {
            public TestWorkstation(Guid id, string pcId)
            {
                Id = id;
                PcId = pcId;
            }
        }

        private class FakeStartSessionHandler : ICommandHandler<StartSessionCommand, SessionResponseDto>
        {
            public Func<StartSessionCommand, Result<SessionResponseDto>>? OnHandle { get; set; }
            public Task<Result<SessionResponseDto>> HandleAsync(StartSessionCommand command, CancellationToken cancellationToken = default)
            {
                if (OnHandle != null) return Task.FromResult(OnHandle(command));
                return Task.FromResult(Result<SessionResponseDto>.Success(new SessionResponseDto
                {
                    SessionId = Guid.NewGuid(),
                    WorkstationId = command.WorkstationId,
                    GamerId = command.GamerId,
                    Status = "ACTIVE"
                }));
            }
        }

        private class FakeStopSessionHandler : ICommandHandler<StopSessionCommand, SessionResponseDto>
        {
            public Func<StopSessionCommand, Result<SessionResponseDto>>? OnHandle { get; set; }
            public Task<Result<SessionResponseDto>> HandleAsync(StopSessionCommand command, CancellationToken cancellationToken = default)
            {
                if (OnHandle != null) return Task.FromResult(OnHandle(command));
                return Task.FromResult(Result<SessionResponseDto>.Success(new SessionResponseDto
                {
                    SessionId = command.SessionId,
                    Status = "ENDED"
                }));
            }
        }

        private class FakePauseSessionHandler : ICommandHandler<PauseSessionCommand, SessionResponseDto>
        {
            public Task<Result<SessionResponseDto>> HandleAsync(PauseSessionCommand command, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Result<SessionResponseDto>.Success(new SessionResponseDto
                {
                    SessionId = command.SessionId,
                    Status = "PAUSED"
                }));
            }
        }

        private class FakeResumeSessionHandler : ICommandHandler<ResumeSessionCommand, SessionResponseDto>
        {
            public Task<Result<SessionResponseDto>> HandleAsync(ResumeSessionCommand command, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Result<SessionResponseDto>.Success(new SessionResponseDto
                {
                    SessionId = command.SessionId,
                    Status = "ACTIVE"
                }));
            }
        }

        private class FakeExtendSessionHandler : ICommandHandler<ExtendSessionCommand, SessionExtensionResponseDto>
        {
            public Task<Result<SessionExtensionResponseDto>> HandleAsync(ExtendSessionCommand command, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Result<SessionExtensionResponseDto>.Success(new SessionExtensionResponseDto
                {
                    SessionExtensionId = Guid.NewGuid(),
                    SessionId = command.SessionId,
                    ExtendedDuration = TimeSpan.FromMinutes(command.AdditionalMinutes),
                    Cost = 15.00m,
                    Currency = "SAY",
                    IdempotencyKey = command.IdempotencyKey ?? "EXT-KEY"
                }));
            }
        }

        private class FakeAuditEventRepository : IRepository<AuditEvent>
        {
            public List<AuditEvent> AddedEvents { get; } = new();

            public Task AddAsync(AuditEvent entity, CancellationToken cancellationToken = default)
            {
                AddedEvents.Add(entity);
                return Task.CompletedTask;
            }

            public Task DeleteAsync(AuditEvent entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public void Delete(AuditEvent entity) { }

            public Task<IReadOnlyList<AuditEvent>> FindAsync(Expression<Func<AuditEvent, bool>> predicate, bool track = false, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyList<AuditEvent>>(new List<AuditEvent>());
            }

            public Task<AuditEvent?> FirstOrDefaultAsync(Expression<Func<AuditEvent, bool>> predicate, bool track = false, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<AuditEvent?>(null);
            }

            public Task<IReadOnlyList<AuditEvent>> GetAllAsync(bool track = false, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyList<AuditEvent>>(AddedEvents);
            }

            public Task<AuditEvent?> GetByIdAsync(Guid id, bool track = false, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<AuditEvent?>(null);
            }

            public void Update(AuditEvent entity) { }
        }

        [Fact]
        public async Task ReconcileAsync_StartSession_InvokesStartSessionHandlerAndReturnsAccepted()
        {
            // Arrange
            var startHandler = new FakeStartSessionHandler();
            var stopHandler = new FakeStopSessionHandler();
            var pauseHandler = new FakePauseSessionHandler();
            var resumeHandler = new FakeResumeSessionHandler();
            var extendHandler = new FakeExtendSessionHandler();
            var auditRepo = new FakeAuditEventRepository();

            var service = new OfflineBusinessReconciliationService(
                startHandler, stopHandler, pauseHandler, resumeHandler, extendHandler, auditRepo, NullLogger<OfflineBusinessReconciliationService>.Instance);

            var gamerId = Guid.NewGuid();
            var workstationId = Guid.NewGuid();
            var workstation = new TestWorkstation(workstationId, "PC-001");

            var item = new OfflineQueueItem
            {
                EventId = Guid.NewGuid().ToString("D"),
                EventType = "SESSION_COMMAND_REQUEST",
                Payload = JsonSerializer.Serialize(new
                {
                    action = "START",
                    gamerId = gamerId,
                    workstationId = workstationId
                })
            };

            // Act
            var result = await service.ReconcileAsync(item, workstation, null, "batch-1");

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(OfflineReconciliationStatus.Accepted, result.Status);
            Assert.Equal("SUCCESS_SESSION_STARTED", result.ReasonCode);
            Assert.NotNull(result.EntityId);
        }

        [Fact]
        public async Task ReconcileAsync_StartSession_WorkstationMismatch_ReturnsRejectWithIdentityMismatch()
        {
            // Arrange
            var service = new OfflineBusinessReconciliationService(
                new FakeStartSessionHandler(), new FakeStopSessionHandler(), new FakePauseSessionHandler(), new FakeResumeSessionHandler(), new FakeExtendSessionHandler(),
                new FakeAuditEventRepository(), NullLogger<OfflineBusinessReconciliationService>.Instance);

            var workstation = new TestWorkstation(Guid.NewGuid(), "PC-001");

            var item = new OfflineQueueItem
            {
                EventId = Guid.NewGuid().ToString("D"),
                EventType = "SESSION_COMMAND_REQUEST",
                Payload = JsonSerializer.Serialize(new
                {
                    action = "START",
                    gamerId = Guid.NewGuid(),
                    workstationId = Guid.NewGuid() // Mismatched workstation ID
                })
            };

            // Act
            var result = await service.ReconcileAsync(item, workstation, null, "batch-1");

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(OfflineReconciliationStatus.Rejected, result.Status);
            Assert.Equal(OfflineReasonCode.IdentityMismatch, result.ReasonCode);
        }

        [Fact]
        public async Task ReconcileAsync_SessionStartFailure_ReturnsConflictWithBusinessReasonCode()
        {
            // Arrange
            var startHandler = new FakeStartSessionHandler
            {
                OnHandle = _ => Result<SessionResponseDto>.Failure("WORKSTATION_HAS_ACTIVE_SESSION", "Workstation already has an active session.")
            };

            var service = new OfflineBusinessReconciliationService(
                startHandler, new FakeStopSessionHandler(), new FakePauseSessionHandler(), new FakeResumeSessionHandler(), new FakeExtendSessionHandler(),
                new FakeAuditEventRepository(), NullLogger<OfflineBusinessReconciliationService>.Instance);

            var workstation = new TestWorkstation(Guid.NewGuid(), "PC-001");

            var item = new OfflineQueueItem
            {
                EventId = Guid.NewGuid().ToString("D"),
                EventType = "SESSION_COMMAND_REQUEST",
                Payload = JsonSerializer.Serialize(new
                {
                    action = "START",
                    gamerId = Guid.NewGuid(),
                    workstationId = workstation.Id
                })
            };

            // Act
            var result = await service.ReconcileAsync(item, workstation, null, "batch-1");

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(OfflineReconciliationStatus.Conflict, result.Status);
            Assert.Equal("SESSION_START_FAILED", result.ReasonCode);
            Assert.Equal("Workstation already has an active session.", result.ErrorMessage);
        }

        [Fact]
        public async Task ReconcileAsync_StopSession_InvokesStopSessionHandlerAndReturnsAccepted()
        {
            // Arrange
            var service = new OfflineBusinessReconciliationService(
                new FakeStartSessionHandler(), new FakeStopSessionHandler(), new FakePauseSessionHandler(), new FakeResumeSessionHandler(), new FakeExtendSessionHandler(),
                new FakeAuditEventRepository(), NullLogger<OfflineBusinessReconciliationService>.Instance);

            var sessionId = Guid.NewGuid();
            var workstation = new TestWorkstation(Guid.NewGuid(), "PC-001");

            var item = new OfflineQueueItem
            {
                EventId = Guid.NewGuid().ToString("D"),
                EventType = "SESSION_COMMAND_REQUEST",
                Payload = JsonSerializer.Serialize(new
                {
                    action = "STOP",
                    sessionId = sessionId
                })
            };

            // Act
            var result = await service.ReconcileAsync(item, workstation, null, "batch-1");

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(OfflineReconciliationStatus.Accepted, result.Status);
            Assert.Equal("SUCCESS_SESSION_STOPPED", result.ReasonCode);
            Assert.Equal(sessionId, result.EntityId);
        }

        [Fact]
        public async Task ReconcileAsync_ExtendSession_InvokesExtendSessionHandlerAndReturnsAccepted()
        {
            // Arrange
            var service = new OfflineBusinessReconciliationService(
                new FakeStartSessionHandler(), new FakeStopSessionHandler(), new FakePauseSessionHandler(), new FakeResumeSessionHandler(), new FakeExtendSessionHandler(),
                new FakeAuditEventRepository(), NullLogger<OfflineBusinessReconciliationService>.Instance);

            var sessionId = Guid.NewGuid();
            var workstation = new TestWorkstation(Guid.NewGuid(), "PC-001");

            var item = new OfflineQueueItem
            {
                EventId = Guid.NewGuid().ToString("D"),
                EventType = "SESSION_COMMAND_REQUEST",
                Payload = JsonSerializer.Serialize(new
                {
                    action = "EXTEND",
                    sessionId = sessionId,
                    additionalMinutes = 60,
                    idempotencyKey = "EXT-123"
                })
            };

            // Act
            var result = await service.ReconcileAsync(item, workstation, null, "batch-1");

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(OfflineReconciliationStatus.Accepted, result.Status);
            Assert.Equal("SUCCESS_SESSION_EXTENDED", result.ReasonCode);
            Assert.Equal(sessionId, result.EntityId);
        }

        [Fact]
        public async Task ReconcileAsync_InformationalEvent_PersistsAuditEventAndReturnsAccepted()
        {
            // Arrange
            var auditRepo = new FakeAuditEventRepository();
            var service = new OfflineBusinessReconciliationService(
                new FakeStartSessionHandler(), new FakeStopSessionHandler(), new FakePauseSessionHandler(), new FakeResumeSessionHandler(), new FakeExtendSessionHandler(),
                auditRepo, NullLogger<OfflineBusinessReconciliationService>.Instance);

            var workstation = new TestWorkstation(Guid.NewGuid(), "PC-001");

            var item = new OfflineQueueItem
            {
                EventId = Guid.NewGuid().ToString("D"),
                EventType = ClientEventType.SecurityEvent,
                ReliabilityClass = EventReliabilityClass.Critical,
                Payload = JsonSerializer.Serialize(new { violationType = "TAMPERING", severity = "HIGH" })
            };

            // Act
            var result = await service.ReconcileAsync(item, workstation, null, "batch-1");

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(OfflineReconciliationStatus.Accepted, result.Status);
            Assert.Equal("SUCCESS_INFORMATIONAL", result.ReasonCode);
            Assert.Single(auditRepo.AddedEvents);
            Assert.Equal("SECURITY_EVENT", auditRepo.AddedEvents[0].EventType);
        }

        [Fact]
        public async Task ReconcileAsync_UnsupportedEventType_ReturnsReject()
        {
            // Arrange
            var service = new OfflineBusinessReconciliationService(
                new FakeStartSessionHandler(), new FakeStopSessionHandler(), new FakePauseSessionHandler(), new FakeResumeSessionHandler(), new FakeExtendSessionHandler(),
                new FakeAuditEventRepository(), NullLogger<OfflineBusinessReconciliationService>.Instance);

            var workstation = new TestWorkstation(Guid.NewGuid(), "PC-001");

            var item = new OfflineQueueItem
            {
                EventId = Guid.NewGuid().ToString("D"),
                EventType = "UNKNOWN_CUSTOM_EVENT",
                Payload = "{}"
            };

            // Act
            var result = await service.ReconcileAsync(item, workstation, null, "batch-1");

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(OfflineReconciliationStatus.Rejected, result.Status);
            Assert.Equal("UNSUPPORTED_EVENT_TYPE", result.ReasonCode);
        }
    }
}

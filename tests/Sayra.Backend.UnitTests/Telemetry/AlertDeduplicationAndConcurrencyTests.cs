using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Domain.Telemetry;
using Sayra.Backend.Domain.ValueObjects;
using Sayra.Backend.Infrastructure.Telemetry;
using Xunit;

namespace Sayra.Backend.UnitTests.Telemetry
{
    public class AlertDeduplicationAndConcurrencyTests
    {
        [Fact]
        public async Task ConcurrentEvaluations_SameWorkstationCondition_DeduplicatesAndIsThreadSafe()
        {
            var store = new ConcurrentDictionary<string, Incident>();
            var repositoryMock = new Mock<IIncidentRepository>();

            repositoryMock
                .Setup(r => r.GetActiveIncidentsForWorkstationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns<string, CancellationToken>((pcId, ct) =>
                {
                    var activeList = store.Values
                        .Where(i => i.PcId == pcId && i.LifecycleState != IncidentLifecycleState.Resolved)
                        .ToList();
                    return Task.FromResult<IReadOnlyList<Incident>>(activeList);
                });

            repositoryMock
                .Setup(r => r.SaveIncidentAsync(It.IsAny<Incident>(), It.IsAny<CancellationToken>()))
                .Returns<Incident, CancellationToken>((inc, ct) =>
                {
                    store.AddOrUpdate(inc.Fingerprint, inc, (k, v) => inc);
                    return Task.CompletedTask;
                });

            var metricsMock = new Mock<IAlertMetrics>();
            var dispatcherMock = new Mock<IAlertNotificationDispatcher>();
            var options = Options.Create(new AlertingOptions { IsEnabled = true });

            var engine = new AlertEvaluationEngine(
                repositoryMock.Object,
                options,
                metricsMock.Object,
                dispatcherMock.Object,
                NullLogger<AlertEvaluationEngine>.Instance);

            var identity = new WorkstationIdentity("PC-CONCURRENT-01", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var now = DateTime.UtcNow;

            var reasons = new List<WorkstationHealthReason>
            {
                new WorkstationHealthReason("CPU_SUSTAINED_HIGH", WorkstationHealthState.Critical, "CPU", 99.0, 80.0, "CPU 99%", now)
            };

            var healthResult = new WorkstationHealthEvaluationResult(
                identity,
                WorkstationHealthState.Critical,
                0.0,
                reasons,
                now,
                "v1.0");

            int workerCount = 20;
            var tasks = new Task<IReadOnlyList<Incident>>[workerCount];

            for (int i = 0; i < workerCount; i++)
            {
                tasks[i] = Task.Run(() => engine.EvaluateHealthResultAsync(healthResult));
            }

            var results = await Task.WhenAll(tasks);

            Assert.Single(store);
            var incident = store.Values.First();
            Assert.Equal("PC-CONCURRENT-01", incident.PcId);
            Assert.Equal("CPU_SUSTAINED_HIGH", incident.RuleCode);
            Assert.Equal(IncidentLifecycleState.Firing, incident.LifecycleState);
            Assert.True(incident.ObservationCount >= 1);
        }
    }
}

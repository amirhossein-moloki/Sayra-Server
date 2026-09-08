using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
    public class AlertStormAndStressTests
    {
        [Fact]
        public async Task AlertStorm_1000WorkstationsOffline_EvaluatesInBoundedTimeAndMemory()
        {
            var store = new ConcurrentDictionary<string, Incident>();
            var repositoryMock = new Mock<IIncidentRepository>();

            repositoryMock
                .Setup(r => r.GetActiveIncidentsForWorkstationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns<string, CancellationToken>((pcId, ct) =>
                {
                    var list = store.Values
                        .Where(i => i.PcId == pcId && i.LifecycleState != IncidentLifecycleState.Resolved)
                        .ToList();
                    return Task.FromResult<IReadOnlyList<Incident>>(list);
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

            int fleetSize = 1000;
            var orgId = Guid.NewGuid();
            var siteId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var healthResults = new List<WorkstationHealthEvaluationResult>(fleetSize);
            for (int i = 0; i < fleetSize; i++)
            {
                string pcId = $"PC-STORM-{i:D4}";
                var identity = new WorkstationIdentity(pcId, Guid.NewGuid(), siteId, orgId);
                var healthResult = WorkstationHealthEvaluationResult.CreateOffline(identity, "Connection lost during storm", now, "v1.0");
                healthResults.Add(healthResult);
            }

            var sw1 = Stopwatch.StartNew();
            foreach (var result in healthResults)
            {
                await engine.EvaluateHealthResultAsync(result);
            }
            sw1.Stop();

            Assert.Equal(fleetSize, store.Count);
            Assert.True(sw1.ElapsedMilliseconds < 5000, $"Wave 1 took {sw1.ElapsedMilliseconds}ms, expected < 5000ms.");

            var sw2 = Stopwatch.StartNew();
            foreach (var result in healthResults)
            {
                await engine.EvaluateHealthResultAsync(result);
            }
            sw2.Stop();

            Assert.Equal(fleetSize, store.Count);
            Assert.All(store.Values, inc => Assert.Equal(2, inc.ObservationCount));
            Assert.True(sw2.ElapsedMilliseconds < 5000, $"Wave 2 took {sw2.ElapsedMilliseconds}ms, expected < 5000ms.");

            var nowRecovery = DateTime.UtcNow;
            var sw3 = Stopwatch.StartNew();
            for (int i = 0; i < fleetSize; i++)
            {
                string pcId = $"PC-STORM-{i:D4}";
                var identity = new WorkstationIdentity(pcId, Guid.NewGuid(), siteId, orgId);
                var healthyResult = WorkstationHealthEvaluationResult.CreateHealthy(identity, nowRecovery, "v1.0");
                await engine.EvaluateHealthResultAsync(healthyResult);
            }
            sw3.Stop();

            Assert.All(store.Values, inc => Assert.Equal(IncidentLifecycleState.Resolved, inc.LifecycleState));
            Assert.True(sw3.ElapsedMilliseconds < 5000, $"Wave 3 recovery took {sw3.ElapsedMilliseconds}ms, expected < 5000ms.");
        }
    }
}

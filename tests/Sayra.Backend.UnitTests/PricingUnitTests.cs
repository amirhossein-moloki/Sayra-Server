using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Pricing;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class PricingUnitTests
    {
        [Fact]
        public void PricingPlan_NormalizeAndValidate_Valid_Plan_Should_Succeed()
        {
            var plan = new PricingPlan
            {
                SiteId = Guid.NewGuid(),
                Name = " Standard Plan ",
                Currency = "say",
                Status = "inactive"
            };

            plan.NormalizeAndValidate();

            Assert.Equal("Standard Plan", plan.Name);
            Assert.Equal("SAY", plan.Currency);
            Assert.Equal("Inactive", plan.Status);
            Assert.False(plan.IsActive);
        }

        [Fact]
        public void PricingPlan_Activate_And_Deactivate_Should_Update_Status()
        {
            var plan = new PricingPlan
            {
                SiteId = Guid.NewGuid(),
                Name = "Test Plan",
                Status = "Inactive"
            };

            Assert.False(plan.IsActive);

            plan.Activate();
            Assert.True(plan.IsActive);
            Assert.Equal("Active", plan.Status);

            plan.Deactivate();
            Assert.False(plan.IsActive);
            Assert.Equal("Inactive", plan.Status);
        }

        [Fact]
        public void PricingPlan_NormalizeAndValidate_Empty_SiteId_Should_Throw()
        {
            var plan = new PricingPlan { SiteId = Guid.Empty, Name = "Plan 1" };
            var ex = Assert.Throws<InvalidDomainException>(() => plan.NormalizeAndValidate());
            Assert.Equal("INVALID_SITE_ID", ex.ErrorCode);
        }

        [Fact]
        public void PricingRule_NormalizeAndValidate_Negative_RateAmount_Should_Throw()
        {
            var rule = new PricingRule
            {
                PricingPlanId = Guid.NewGuid(),
                Name = "Rule 1",
                RateAmount = -5.00m,
                Priority = 1
            };

            var ex = Assert.Throws<InvalidDomainException>(() => rule.NormalizeAndValidate());
            Assert.Equal("INVALID_RATE_AMOUNT", ex.ErrorCode);
        }

        [Fact]
        public void PricingRule_Matches_Workstation_Dimension()
        {
            var targetWsId = Guid.NewGuid();
            var otherWsId = Guid.NewGuid();
            var siteId = Guid.NewGuid();

            var rule = new PricingRule
            {
                PricingPlanId = Guid.NewGuid(),
                Name = "Workstation Rule",
                RateAmount = 15.00m,
                Priority = 1,
                WorkstationId = targetWsId
            };

            var now = DateTime.UtcNow;

            Assert.True(rule.Matches(siteId, null, targetWsId, null, now));
            Assert.False(rule.Matches(siteId, null, otherWsId, null, now));
            Assert.False(rule.Matches(siteId, null, null, null, now));
        }

        [Fact]
        public void PricingRule_Matches_Zone_Dimension()
        {
            var targetZoneId = Guid.NewGuid();
            var otherZoneId = Guid.NewGuid();
            var siteId = Guid.NewGuid();

            var rule = new PricingRule
            {
                PricingPlanId = Guid.NewGuid(),
                Name = "Zone Rule",
                RateAmount = 12.00m,
                Priority = 2,
                ZoneId = targetZoneId
            };

            var now = DateTime.UtcNow;

            Assert.True(rule.Matches(siteId, targetZoneId, null, null, now));
            Assert.False(rule.Matches(siteId, otherZoneId, null, null, now));
            Assert.False(rule.Matches(siteId, null, null, null, now));
        }

        [Fact]
        public void PricingRule_Matches_GamerType_Dimension()
        {
            var rule = new PricingRule
            {
                PricingPlanId = Guid.NewGuid(),
                Name = "VIP Gamer Rule",
                RateAmount = 8.00m,
                Priority = 1,
                GamerType = "VIP"
            };

            var now = DateTime.UtcNow;

            Assert.True(rule.Matches(Guid.NewGuid(), null, null, "vip", now));
            Assert.True(rule.Matches(Guid.NewGuid(), null, null, "VIP", now));
            Assert.False(rule.Matches(Guid.NewGuid(), null, null, "STANDARD", now));
            Assert.False(rule.Matches(Guid.NewGuid(), null, null, null, now));
        }

        [Fact]
        public void PricingRule_Matches_DayOfWeek_Dimension()
        {
            var rule = new PricingRule
            {
                PricingPlanId = Guid.NewGuid(),
                Name = "Weekend Rule",
                RateAmount = 20.00m,
                Priority = 1,
                DayOfWeek = DayOfWeek.Saturday
            };

            // 2026-08-15 is Saturday
            var saturday = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
            // 2026-08-16 is Sunday
            var sunday = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);

            Assert.True(rule.Matches(Guid.NewGuid(), null, null, null, saturday));
            Assert.False(rule.Matches(Guid.NewGuid(), null, null, null, sunday));
        }

        [Fact]
        public void PricingRule_Matches_TimeRange_Dimension()
        {
            var rule = new PricingRule
            {
                PricingPlanId = Guid.NewGuid(),
                Name = "Peak Time Rule",
                RateAmount = 18.00m,
                Priority = 1,
                StartTime = new TimeSpan(14, 0, 0), // 14:00
                EndTime = new TimeSpan(18, 0, 0)   // 18:00
            };

            var at15 = new DateTime(2026, 8, 16, 15, 30, 0, DateTimeKind.Utc);
            var at19 = new DateTime(2026, 8, 16, 19, 0, 0, DateTimeKind.Utc);

            Assert.True(rule.Matches(Guid.NewGuid(), null, null, null, at15));
            Assert.False(rule.Matches(Guid.NewGuid(), null, null, null, at19));
        }

        [Fact]
        public async Task RateResolver_Priority_Ordering_First_Match_Wins()
        {
            var siteId = Guid.NewGuid();
            var wsId = Guid.NewGuid();
            var activePlan = new PricingPlan
            {
                SiteId = siteId,
                Name = "Active Plan",
                Status = "Active"
            };

            var rule1Specific = new PricingRule
            {
                PricingPlanId = activePlan.PricingPlanId,
                Name = "Workstation Specific Rule",
                RateAmount = 25.00m,
                Priority = 1,
                WorkstationId = wsId
            };

            var rule2Default = new PricingRule
            {
                PricingPlanId = activePlan.PricingPlanId,
                Name = "Site Default Rule",
                RateAmount = 10.00m,
                Priority = 2
            };

            var mockPlanRepo = new Mock<IRepository<PricingPlan>>();
            var mockRuleRepo = new Mock<IRepository<PricingRule>>();
            var mockWsRepo = new Mock<IRepository<Workstation>>();

            mockPlanRepo.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PricingPlan, bool>>>(),
                false,
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<PricingPlan> { activePlan });

            mockRuleRepo.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PricingRule, bool>>>(),
                false,
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<PricingRule> { rule2Default, rule1Specific }); // Out of order, should be sorted by priority 1 then 2

            var resolver = new RateResolver(mockPlanRepo.Object, mockRuleRepo.Object, mockWsRepo.Object);

            var result = await resolver.ResolveRateAsync(new ResolveRateRequestDto
            {
                SiteId = siteId,
                WorkstationId = wsId,
                Timestamp = DateTime.UtcNow
            });

            Assert.Equal(25.00m, result.RateAmount);
            Assert.Equal("Workstation Specific Rule", result.RuleReference);
            Assert.Equal(1, result.Priority);
        }

        [Fact]
        public async Task RateResolver_No_Active_Plan_Should_Throw()
        {
            var siteId = Guid.NewGuid();
            var mockPlanRepo = new Mock<IRepository<PricingPlan>>();
            var mockRuleRepo = new Mock<IRepository<PricingRule>>();
            var mockWsRepo = new Mock<IRepository<Workstation>>();

            mockPlanRepo.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PricingPlan, bool>>>(),
                false,
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<PricingPlan>()); // No active plan

            var resolver = new RateResolver(mockPlanRepo.Object, mockRuleRepo.Object, mockWsRepo.Object);

            var ex = await Assert.ThrowsAsync<InvalidDomainException>(() => resolver.ResolveRateAsync(new ResolveRateRequestDto
            {
                SiteId = siteId
            }));

            Assert.Equal("PRICING_PLAN_NOT_FOUND", ex.ErrorCode);
        }

        [Fact]
        public async Task RateSnapshotService_Creation_And_Immutability()
        {
            var sessionId = Guid.NewGuid();
            var planId = Guid.NewGuid();
            var ruleId = Guid.NewGuid();

            var mockRepo = new Mock<IRepository<RateSnapshot>>();
            var mockUow = new Mock<IUnitOfWork>();

            var existingSnapshots = new List<RateSnapshot>();

            mockRepo.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<RateSnapshot, bool>>>(),
                false,
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingSnapshots);

            mockRepo.Setup(r => r.AddAsync(It.IsAny<RateSnapshot>(), It.IsAny<CancellationToken>()))
                .Callback<RateSnapshot, CancellationToken>((s, _) => existingSnapshots.Add(s))
                .Returns(Task.CompletedTask);

            var service = new RateSnapshotService(mockRepo.Object, mockUow.Object);

            var snapshot1 = await service.CreateSnapshotAsync(
                sessionId, planId, ruleId, 15.5000m, "SAY", "Rule Snapshot 1", DateTime.UtcNow);

            Assert.Equal(sessionId, snapshot1.SessionId);
            Assert.Equal(15.5000m, snapshot1.RateAmount);
            Assert.Equal("Rule Snapshot 1", snapshot1.RuleReference);

            // Attempt second snapshot creation for same session: Must return existing snapshot (immutable!)
            var snapshot2 = await service.CreateSnapshotAsync(
                sessionId, planId, ruleId, 99.9999m, "SAY", "Modified Rule", DateTime.UtcNow);

            Assert.Equal(15.5000m, snapshot2.RateAmount); // Did not change!
            Assert.Equal("Rule Snapshot 1", snapshot2.RuleReference);
        }

        [Fact]
        public void RateSnapshot_NormalizeAndValidate_Rounds_RateAmount_To_4_Decimals()
        {
            var snapshot = new RateSnapshot
            {
                SessionId = Guid.NewGuid(),
                PricingPlanId = Guid.NewGuid(),
                RateAmount = 10.123456m,
                Currency = "SAY",
                RuleReference = "Test"
            };

            snapshot.NormalizeAndValidate();

            Assert.Equal(10.1235m, snapshot.RateAmount);
        }
    }
}

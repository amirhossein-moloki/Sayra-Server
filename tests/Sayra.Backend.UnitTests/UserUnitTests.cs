using System;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class UserUnitTests
    {
        [Fact]
        public void NormalizeAndValidate_ShouldGenerateStableUserId_AndNormalizeFields()
        {
            var user = new User
            {
                Username = "   gamerOne   ",
                Email = "  GAMER1@EXAMPLE.COM  ",
                DisplayName = " Gamer One ",
                Role = UserRole.Gamer,
                Status = UserAccountState.Active
            };

            user.NormalizeAndValidate();

            Assert.Equal("gamerOne", user.Username);
            Assert.Equal("gamer1@example.com", user.Email);
            Assert.Equal("Gamer One", user.DisplayName);
            Assert.StartsWith("USR-", user.UserId);
            Assert.Equal(12, user.UserId.Length); // "USR-" + 8 hex chars
        }

        [Fact]
        public void NormalizeAndValidate_EmptyUsername_ShouldThrowInvalidDomainException()
        {
            var user = new User
            {
                Username = "   ",
                Email = "test@example.com"
            };

            var ex = Assert.Throws<InvalidDomainException>(() => user.NormalizeAndValidate());
            Assert.Equal("INVALID_USERNAME", ex.ErrorCode);
        }

        [Fact]
        public void TransitionTo_ValidStateTransitions_ShouldSucceed()
        {
            var user = new User { Username = "user1", Status = UserAccountState.Pending };

            // Pending -> Active
            user.TransitionTo(UserAccountState.Active);
            Assert.Equal(UserAccountState.Active, user.Status);

            // Active -> Suspended
            user.TransitionTo(UserAccountState.Suspended);
            Assert.Equal(UserAccountState.Suspended, user.Status);

            // Suspended -> Active
            user.TransitionTo(UserAccountState.Active);
            Assert.Equal(UserAccountState.Active, user.Status);

            // Active -> Locked
            user.TransitionTo(UserAccountState.Locked);
            Assert.Equal(UserAccountState.Locked, user.Status);

            // Locked -> Active
            user.TransitionTo(UserAccountState.Active);
            Assert.Equal(UserAccountState.Active, user.Status);

            // Active -> Disabled
            user.TransitionTo(UserAccountState.Disabled);
            Assert.Equal(UserAccountState.Disabled, user.Status);

            // Disabled -> Active
            user.TransitionTo(UserAccountState.Active);
            Assert.Equal(UserAccountState.Active, user.Status);

            // Active -> Deleted
            user.TransitionTo(UserAccountState.Deleted);
            Assert.Equal(UserAccountState.Deleted, user.Status);
        }

        [Theory]
        [InlineData(UserAccountState.Pending, UserAccountState.Suspended)]
        [InlineData(UserAccountState.Pending, UserAccountState.Locked)]
        [InlineData(UserAccountState.Pending, UserAccountState.Disabled)]
        [InlineData(UserAccountState.Deleted, UserAccountState.Active)]
        [InlineData(UserAccountState.Disabled, UserAccountState.Locked)]
        public void TransitionTo_InvalidTransitions_ShouldThrowInvalidDomainException(UserAccountState fromState, UserAccountState toState)
        {
            var user = new User { Username = "user1", Status = fromState };

            var ex = Assert.Throws<InvalidDomainException>(() => user.TransitionTo(toState));
            Assert.Equal("INVALID_ACCOUNT_STATE_TRANSITION", ex.ErrorCode);
        }

        [Fact]
        public void RecordFailedLoginAttempt_ShouldLockout_WhenThresholdReached()
        {
            var user = new User { Username = "user1", Status = UserAccountState.Active };

            for (int i = 0; i < 4; i++)
            {
                user.RecordFailedLoginAttempt(maxAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15));
                Assert.False(user.IsCurrentlyLockedOut());
            }

            user.RecordFailedLoginAttempt(maxAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15));
            Assert.True(user.IsCurrentlyLockedOut());
            Assert.Equal(UserAccountState.Locked, user.Status);
            Assert.NotNull(user.LockedUntil);
        }

        [Fact]
        public void Unlock_ShouldResetLockoutState()
        {
            var user = new User { Username = "user1", Status = UserAccountState.Active };
            user.RecordFailedLoginAttempt(maxAttempts: 1, lockoutDuration: TimeSpan.FromMinutes(15));

            Assert.True(user.IsCurrentlyLockedOut());

            user.Unlock();

            Assert.False(user.IsCurrentlyLockedOut());
            Assert.Equal(UserAccountState.Active, user.Status);
            Assert.Equal(0, user.FailedLoginAttempts);
            Assert.Null(user.LockedUntil);
        }
    }
}

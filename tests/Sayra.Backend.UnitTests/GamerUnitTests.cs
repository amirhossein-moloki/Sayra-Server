using System;
using Xunit;
using Sayra.Backend.Application.Gamers;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Infrastructure.Security;

namespace Sayra.Backend.UnitTests
{
    public class GamerUnitTests
    {
        [Fact]
        public void Gamer_NormalizeAndValidate_Should_Trim_And_Normalize_Fields()
        {
            // Arrange
            var gamer = new Gamer
            {
                Username = "  JohnGamer  ",
                Email = "  JOHN.DOE@EXAMPLE.COM  ",
                PhoneNumber = " +1234567890 ",
                FirstName = " John ",
                LastName = " Doe ",
                Status = "active"
            };

            // Act
            gamer.NormalizeAndValidate();

            // Assert
            Assert.Equal("JohnGamer", gamer.Username);
            Assert.Equal("john.doe@example.com", gamer.Email);
            Assert.Equal("+1234567890", gamer.PhoneNumber);
            Assert.Equal("John", gamer.FirstName);
            Assert.Equal("Doe", gamer.LastName);
            Assert.Equal("Active", gamer.Status);
            Assert.StartsWith("GMR-", gamer.GamerId);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Gamer_Empty_Username_Should_Throw_InvalidDomainException(string username)
        {
            var gamer = new Gamer
            {
                Username = username,
                Email = "test@example.com"
            };

            var ex = Assert.Throws<InvalidDomainException>(() => gamer.NormalizeAndValidate());
            Assert.Equal("INVALID_USERNAME", ex.ErrorCode);
        }

        [Theory]
        [InlineData("")]
        [InlineData("invalid-email")]
        [InlineData("testexample.com")]
        public void Gamer_Invalid_Email_Should_Throw_InvalidDomainException(string email)
        {
            var gamer = new Gamer
            {
                Username = "TestUser",
                Email = email
            };

            var ex = Assert.Throws<InvalidDomainException>(() => gamer.NormalizeAndValidate());
            Assert.Equal("INVALID_EMAIL", ex.ErrorCode);
        }

        [Fact]
        public void Gamer_Status_Transitions_Should_Work_Correctly()
        {
            var gamer = new Gamer
            {
                Username = "StatusGamer",
                Email = "status@example.com",
                Status = "Active"
            };

            Assert.True(gamer.CanOperate());

            gamer.Deactivate();
            Assert.Equal("Inactive", gamer.Status);
            Assert.False(gamer.CanOperate());

            gamer.Suspend();
            Assert.Equal("Suspended", gamer.Status);
            Assert.False(gamer.CanOperate());

            gamer.Ban();
            Assert.Equal("Banned", gamer.Status);
            Assert.False(gamer.CanOperate());

            gamer.Activate();
            Assert.Equal("Active", gamer.Status);
            Assert.True(gamer.CanOperate());
        }

        [Fact]
        public void PasswordHasher_HashAndVerify_Should_Succeed_For_Valid_Password()
        {
            // Arrange
            var hasher = new PasswordHasher();
            string plainPassword = "SecurePassword123!";

            // Act
            var (hash, salt) = hasher.HashPassword(plainPassword);

            // Assert
            Assert.False(string.IsNullOrWhiteSpace(hash));
            Assert.False(string.IsNullOrWhiteSpace(salt));

            Assert.True(hasher.VerifyPassword(plainPassword, hash, salt));
            Assert.False(hasher.VerifyPassword("WrongPassword", hash, salt));
        }

        [Fact]
        public void GamerCredential_SetPassword_And_Lockout_Behavior()
        {
            var credential = new GamerCredential
            {
                GamerEntityId = Guid.NewGuid()
            };

            var hasher = new PasswordHasher();
            var (hash, salt) = hasher.HashPassword("InitialPassword");

            credential.SetPassword(hash, salt);

            Assert.Equal(hash, credential.PasswordHash);
            Assert.Equal(salt, credential.PasswordSalt);
            Assert.Equal(0, credential.FailedAttemptCount);
            Assert.False(credential.IsCurrentlyLockedOut());

            // Simulate 5 failed login attempts
            for (int i = 0; i < 5; i++)
            {
                credential.RecordFailedAttempt(maxAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15));
            }

            Assert.True(credential.IsCurrentlyLockedOut());
            Assert.Equal(5, credential.FailedAttemptCount);

            // Reset lockout
            credential.Unlock();
            Assert.False(credential.IsCurrentlyLockedOut());
            Assert.Equal(0, credential.FailedAttemptCount);
        }

        [Fact]
        public void GamerAccount_NormalizeAndValidate_Should_Set_Default_Currency_And_AccountNumber()
        {
            var gamerId = Guid.NewGuid();
            var account = new GamerAccount
            {
                GamerEntityId = gamerId,
                Status = "active"
            };

            account.NormalizeAndValidate();

            Assert.Equal("Active", account.Status);
            Assert.Equal("SAY", account.Currency);
            Assert.StartsWith("ACC-", account.AccountNumber);
            Assert.True(account.CanTransact());

            account.Freeze();
            Assert.Equal("Frozen", account.Status);
            Assert.False(account.CanTransact());
        }

        [Fact]
        public void CreateGamerCommandValidator_Should_Validate_Input_Fields()
        {
            var validator = new CreateGamerCommandValidator();

            var invalidCommand = new CreateGamerCommand
            {
                Username = "a", // Too short
                Email = "invalid-email",
                Password = "123" // Too short
            };

            var result = validator.Validate(invalidCommand);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == "Username");
            Assert.Contains(result.Errors, e => e.PropertyName == "Email");
            Assert.Contains(result.Errors, e => e.PropertyName == "Password");

            var validCommand = new CreateGamerCommand
            {
                Username = "ValidGamer",
                Email = "valid@example.com",
                Password = "Password123!"
            };

            var validResult = validator.Validate(validCommand);
            Assert.True(validResult.IsValid);
        }
    }
}

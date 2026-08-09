using System;
using Xunit;
using Sayra.Backend.Shared;
using Sayra.Backend.Application.Examples;

namespace Sayra.Backend.UnitTests
{
    public class UnitTests
    {
        [Fact]
        public void Money_Should_Preserve_Precision_Without_Floating_Point_Issues()
        {
            var m1 = new Money(10.00005m, "SAY"); // Rounded to 4 decimals -> 10.0001
            var m2 = new Money(5.12344m, "SAY");  // Rounded to 4 decimals -> 5.1234

            Assert.Equal(10.0001m, m1.Amount);
            Assert.Equal(5.1234m, m2.Amount);

            var result = m1 + m2;
            Assert.Equal(15.1235m, result.Amount);
            Assert.Equal("SAY", result.Currency);
        }

        [Fact]
        public void Money_Arithmetic_On_Different_Currencies_Should_Throw_Exception()
        {
            var m1 = new Money(10.00m, "SAY");
            var m2 = new Money(5.00m, "USD");

            Assert.Throws<InvalidOperationException>(() => m1 + m2);
            Assert.Throws<InvalidOperationException>(() => m1 - m2);
        }

        [Fact]
        public void Result_Should_Represent_Success_State_Correctly()
        {
            var result = Result.Success();
            Assert.True(result.IsSuccess);
            Assert.False(result.IsFailure);
            Assert.Null(result.ErrorCode);
        }

        [Fact]
        public void Result_Should_Represent_Failure_State_Correctly()
        {
            var result = Result.Failure("INVALID_COMMAND", "Command payload was null.");
            Assert.False(result.IsSuccess);
            Assert.True(result.IsFailure);
            Assert.Equal("INVALID_COMMAND", result.ErrorCode);
            Assert.Equal("Command payload was null.", result.ErrorMessage);
        }

        [Fact]
        public void CreateWorkstationCommandValidator_Should_Identify_Invalid_IpAddress()
        {
            var validator = new CreateWorkstationCommandValidator();
            var command = new CreateWorkstationCommand
            {
                Name = "PC-01",
                IpAddress = "invalid-ip-format"
            };

            var validationResult = validator.Validate(command);
            Assert.False(validationResult.IsValid);
            Assert.Contains(validationResult.Errors, e => e.PropertyName == "IpAddress");
        }

        [Fact]
        public void CreateWorkstationCommandValidator_Should_Pass_For_Valid_Payload()
        {
            var validator = new CreateWorkstationCommandValidator();
            var command = new CreateWorkstationCommand
            {
                Name = "PC-01",
                IpAddress = "192.168.1.100"
            };

            var validationResult = validator.Validate(command);
            Assert.True(validationResult.IsValid);
        }
    }
}

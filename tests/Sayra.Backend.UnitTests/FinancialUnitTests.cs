using System;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class FinancialUnitTests
    {
        [Fact]
        public void GamerAccount_Credit_ValidAmount_IncreasesBalanceAndReturnsLedgerEntry()
        {
            var account = new GamerAccount
            {
                GamerEntityId = Guid.NewGuid(),
                Status = "Active",
                Currency = "SAY",
                Balance = 100.00m
            };

            var money = new Money(50.00m, "SAY");
            var entry = account.Credit(money, "REF-001", "DEPOSIT", "CORR-001", "admin", "Top up account");

            Assert.Equal(150.00m, account.Balance);
            Assert.NotNull(entry);
            Assert.Equal(account.Id, entry.GamerAccountId);
            Assert.Equal(50.00m, entry.Amount);
            Assert.Equal("SAY", entry.Currency);
            Assert.Equal("CREDIT", entry.Direction);
            Assert.Equal("DEPOSIT", entry.EntryType);
            Assert.Equal("REF-001", entry.Reference);
            Assert.Equal("CORR-001", entry.CorrelationId);
            Assert.Equal(150.00m, entry.BalanceAfter);
        }

        [Fact]
        public void GamerAccount_Debit_SufficientBalance_DecreasesBalanceAndReturnsLedgerEntry()
        {
            var account = new GamerAccount
            {
                GamerEntityId = Guid.NewGuid(),
                Status = "Active",
                Currency = "SAY",
                Balance = 100.00m
            };

            var money = new Money(40.00m, "SAY");
            var entry = account.Debit(money, "REF-002", "WITHDRAWAL", "CORR-002", "gamer", "Withdraw funds");

            Assert.Equal(60.00m, account.Balance);
            Assert.NotNull(entry);
            Assert.Equal(account.Id, entry.GamerAccountId);
            Assert.Equal(40.00m, entry.Amount);
            Assert.Equal("SAY", entry.Currency);
            Assert.Equal("DEBIT", entry.Direction);
            Assert.Equal("WITHDRAWAL", entry.EntryType);
            Assert.Equal("REF-002", entry.Reference);
            Assert.Equal("CORR-002", entry.CorrelationId);
            Assert.Equal(60.00m, entry.BalanceAfter);
        }

        [Fact]
        public void GamerAccount_Debit_InsufficientBalance_ThrowsException()
        {
            var account = new GamerAccount
            {
                GamerEntityId = Guid.NewGuid(),
                Status = "Active",
                Currency = "SAY",
                Balance = 30.00m
            };

            var money = new Money(50.00m, "SAY");

            var ex = Assert.Throws<InvalidDomainException>(() => account.Debit(money, "REF-003", "WITHDRAWAL"));
            Assert.Equal("INSUFFICIENT_BALANCE", ex.ErrorCode);
            Assert.Equal(30.00m, account.Balance); // Balance remains unchanged
        }

        [Fact]
        public void GamerAccount_Credit_CurrencyMismatch_ThrowsException()
        {
            var account = new GamerAccount
            {
                GamerEntityId = Guid.NewGuid(),
                Status = "Active",
                Currency = "SAY",
                Balance = 100.00m
            };

            var money = new Money(50.00m, "USD");

            var ex = Assert.Throws<InvalidDomainException>(() => account.Credit(money, "REF-004"));
            Assert.Equal("CURRENCY_MISMATCH", ex.ErrorCode);
        }

        [Fact]
        public void GamerAccount_Credit_NegativeOrZeroAmount_ThrowsException()
        {
            var account = new GamerAccount
            {
                GamerEntityId = Guid.NewGuid(),
                Status = "Active",
                Currency = "SAY",
                Balance = 100.00m
            };

            var zeroMoney = new Money(0.00m, "SAY");
            var ex = Assert.Throws<InvalidDomainException>(() => account.Credit(zeroMoney, "REF-005"));
            Assert.Equal("INVALID_AMOUNT", ex.ErrorCode);
        }

        [Fact]
        public void GamerAccount_DisabledAccount_CannotTransact()
        {
            var account = new GamerAccount
            {
                GamerEntityId = Guid.NewGuid(),
                Status = "Frozen",
                Currency = "SAY",
                Balance = 100.00m
            };

            Assert.False(account.CanTransact());

            var money = new Money(10.00m, "SAY");
            var exCredit = Assert.Throws<InvalidDomainException>(() => account.Credit(money, "REF-006"));
            Assert.Equal("ACCOUNT_DISABLED", exCredit.ErrorCode);

            var exDebit = Assert.Throws<InvalidDomainException>(() => account.Debit(money, "REF-007"));
            Assert.Equal("ACCOUNT_DISABLED", exDebit.ErrorCode);
        }

        [Fact]
        public void LedgerEntry_NormalizeAndValidate_ValidatesRequiredFields()
        {
            var entry = new LedgerEntry
            {
                GamerAccountId = Guid.Empty,
                Amount = 0.00m,
                Currency = "SAY",
                Direction = "INVALID",
                Reference = "REF-008"
            };

            var ex1 = Assert.Throws<InvalidDomainException>(() => entry.NormalizeAndValidate());
            Assert.Equal("INVALID_ACCOUNT_ID", ex1.ErrorCode);

            entry.GamerAccountId = Guid.NewGuid();
            var ex2 = Assert.Throws<InvalidDomainException>(() => entry.NormalizeAndValidate());
            Assert.Equal("INVALID_AMOUNT", ex2.ErrorCode);

            entry.Amount = 25.00m;
            var ex3 = Assert.Throws<InvalidDomainException>(() => entry.NormalizeAndValidate());
            Assert.Equal("INVALID_DIRECTION", ex3.ErrorCode);

            entry.Direction = "CREDIT";
            entry.NormalizeAndValidate(); // Should pass now

            Assert.Equal("SAY", entry.Currency);
            Assert.Equal("CREDIT", entry.Direction);
            Assert.Equal("GENERAL", entry.EntryType);
        }
    }
}

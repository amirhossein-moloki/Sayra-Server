using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sayra.Backend.Api;
using Sayra.Backend.Application.Financial;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Shared;
using Xunit;

namespace Sayra.Backend.IntegrationTests
{
    public class FinancialIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public FinancialIntegrationTests(WebApplicationFactory<Program> factory)
        {
            EnvLoader.Load();
            _factory = factory;
            TestAdminSeeder.EnsureAdminUserCreatedAsync(factory).GetAwaiter().GetResult();
            _client = factory.CreateClient();
            _client.DefaultRequestHeaders.Add("X-User-Id", TestAdminSeeder.AdminUserId.ToString());
            _client.DefaultRequestHeaders.Add("X-User-Role", "Administrator");
        }

        [Fact]
        public async Task Account_Balance_And_Ledger_API_Query_Flow()
        {
            // 1. Create a Gamer
            string uniqueKey = Guid.NewGuid().ToString("N").Substring(0, 8);
            var createGamerRequest = new CreateGamerRequestDto
            {
                Username = $"fin_gamer_{uniqueKey}",
                Email = $"fin_gamer_{uniqueKey}@example.com",
                Password = "Password123!"
            };

            var createGamerResponse = await _client.PostAsJsonAsync("/api/gamers", createGamerRequest);
            Assert.Equal(HttpStatusCode.Created, createGamerResponse.StatusCode);
            var gamer = await createGamerResponse.Content.ReadFromJsonAsync<GamerResponseDto>();
            Assert.NotNull(gamer);

            // 2. Query Balance via GET /api/accounts/{gamerId}/balance
            var balanceResponse = await _client.GetAsync($"/api/accounts/{gamer.Id}/balance");
            Assert.Equal(HttpStatusCode.OK, balanceResponse.StatusCode);
            var balanceDto = await balanceResponse.Content.ReadFromJsonAsync<AccountBalanceResponseDto>();
            Assert.NotNull(balanceDto);
            Assert.Equal(gamer.Id, balanceDto.GamerEntityId);
            Assert.Equal(0.00m, balanceDto.Balance);
            Assert.Equal("SAY", balanceDto.Currency);
            Assert.Equal("Active", balanceDto.Status);

            // 3. Query Ledger via GET /api/accounts/{gamerId}/ledger (initially empty)
            var ledgerResponse = await _client.GetAsync($"/api/accounts/{gamer.Id}/ledger");
            Assert.Equal(HttpStatusCode.OK, ledgerResponse.StatusCode);
            var ledgerDtoList = await ledgerResponse.Content.ReadFromJsonAsync<List<LedgerEntryResponseDto>>();
            Assert.NotNull(ledgerDtoList);
            Assert.Empty(ledgerDtoList);
        }

        [Fact]
        public async Task FinancialAccountService_Credit_Debit_And_Idempotency_Flow()
        {
            using var scope = _factory.Services.CreateScope();
            var financialService = scope.ServiceProvider.GetRequiredService<IFinancialAccountService>();

            // 1. Create Gamer
            string uniqueKey = Guid.NewGuid().ToString("N").Substring(0, 8);
            var createGamerRequest = new CreateGamerRequestDto
            {
                Username = $"fin_svc_{uniqueKey}",
                Email = $"fin_svc_{uniqueKey}@example.com",
                Password = "Password123!"
            };

            var createGamerResponse = await _client.PostAsJsonAsync("/api/gamers", createGamerRequest);
            var gamer = await createGamerResponse.Content.ReadFromJsonAsync<GamerResponseDto>();
            Assert.NotNull(gamer);

            var accountResult = await financialService.GetAccountByGamerIdAsync(gamer.Id);
            Assert.True(accountResult.IsSuccess, accountResult.ErrorMessage);
            var account = accountResult.Value!;

            // 2. Credit account 100 SAY
            string ref1 = $"REF-CREDIT-{uniqueKey}";
            var creditResult = await financialService.CreditAccountAsync(
                account.Id,
                new Money(100.00m, "SAY"),
                ref1,
                "DEPOSIT",
                "CORR-100",
                "admin",
                "Initial deposit");

            Assert.True(creditResult.IsSuccess, creditResult.ErrorMessage);
            var creditEntry = creditResult.Value!;
            Assert.Equal(100.00m, creditEntry.BalanceAfter);
            Assert.Equal("CREDIT", creditEntry.Direction);

            // 3. Attempt duplicate credit with same reference -> must fail (idempotency foundation)
            var dupCreditResult = await financialService.CreditAccountAsync(
                account.Id,
                new Money(100.00m, "SAY"),
                ref1,
                "DEPOSIT",
                "CORR-100",
                "admin",
                "Duplicate deposit attempt");

            Assert.False(dupCreditResult.IsSuccess);
            Assert.Equal("DUPLICATE_OPERATION", dupCreditResult.ErrorCode);

            // 4. Debit account 30 SAY
            string ref2 = $"REF-DEBIT-{uniqueKey}";
            var debitResult = await financialService.DebitAccountAsync(
                account.Id,
                new Money(30.00m, "SAY"),
                ref2,
                "WITHDRAWAL",
                "CORR-200",
                "gamer",
                "Store purchase");

            Assert.True(debitResult.IsSuccess, debitResult.ErrorMessage);
            var debitEntry = debitResult.Value!;
            Assert.Equal(70.00m, debitEntry.BalanceAfter);
            Assert.Equal("DEBIT", debitEntry.Direction);

            // 5. Query updated balance via Service
            var balanceResult = await financialService.GetBalanceAsync(gamer.Id);
            Assert.True(balanceResult.IsSuccess, balanceResult.ErrorMessage);
            Assert.Equal(70.00m, balanceResult.Value!.Amount);

            // 6. Query Ledger via Service
            var ledgerResult = await financialService.GetLedgerAsync(account.Id);
            Assert.True(ledgerResult.IsSuccess, ledgerResult.ErrorMessage);
            var entries = ledgerResult.Value!;
            Assert.Equal(2, entries.Count);
            Assert.Equal(debitEntry.Id, entries[0].Id); // Most recent first
            Assert.Equal(creditEntry.Id, entries[1].Id);
        }

        [Fact]
        public async Task Concurrent_Debits_On_Same_Account_Preserves_Financial_Invariants()
        {
            using var scope = _factory.Services.CreateScope();
            var financialService = scope.ServiceProvider.GetRequiredService<IFinancialAccountService>();

            // 1. Create Gamer & top up 100 SAY
            string uniqueKey = Guid.NewGuid().ToString("N").Substring(0, 8);
            var createGamerRequest = new CreateGamerRequestDto
            {
                Username = $"concurrent_{uniqueKey}",
                Email = $"concurrent_{uniqueKey}@example.com",
                Password = "Password123!"
            };

            var createGamerResponse = await _client.PostAsJsonAsync("/api/gamers", createGamerRequest);
            var gamer = await createGamerResponse.Content.ReadFromJsonAsync<GamerResponseDto>();
            Assert.NotNull(gamer);

            var accountResult = await financialService.GetAccountByGamerIdAsync(gamer.Id);
            Assert.True(accountResult.IsSuccess, accountResult.ErrorMessage);
            var account = accountResult.Value!;

            var initialCredit = await financialService.CreditAccountAsync(
                account.Id,
                new Money(100.00m, "SAY"),
                $"REF-INIT-{uniqueKey}",
                "DEPOSIT");
            Assert.True(initialCredit.IsSuccess, initialCredit.ErrorMessage);

            // 2. Perform two concurrent debit requests: 80 SAY and 50 SAY (Total 130 > 100)
            var task1 = Task.Run(async () =>
            {
                using var s1 = _factory.Services.CreateScope();
                var svc = s1.ServiceProvider.GetRequiredService<IFinancialAccountService>();
                return await svc.DebitAccountAsync(account.Id, new Money(80.00m, "SAY"), $"REF-CONC-1-{uniqueKey}", "SESSION_PAYMENT");
            });

            var task2 = Task.Run(async () =>
            {
                using var s2 = _factory.Services.CreateScope();
                var svc = s2.ServiceProvider.GetRequiredService<IFinancialAccountService>();
                return await svc.DebitAccountAsync(account.Id, new Money(50.00m, "SAY"), $"REF-CONC-2-{uniqueKey}", "SESSION_PAYMENT");
            });

            var results = await Task.WhenAll(task1, task2);

            int successCount = results.Count(r => r.IsSuccess);

            Assert.True(successCount >= 1, "At least one debit should succeed.");

            // Check final balance in DB
            using var scopeCheck = _factory.Services.CreateScope();
            var checkSvc = scopeCheck.ServiceProvider.GetRequiredService<IFinancialAccountService>();
            var finalAccount = await checkSvc.GetAccountByIdAsync(account.Id);
            Assert.True(finalAccount.IsSuccess, finalAccount.ErrorMessage);

            // Balance must be >= 0 (no negative balance)
            Assert.True(finalAccount.Value!.Balance >= 0.00m);

            if (successCount == 1)
            {
                Assert.True(finalAccount.Value.Balance == 20.00m || finalAccount.Value.Balance == 50.00m);
            }

            // Verify Ledger entries match final balance
            var ledgerResult = await checkSvc.GetLedgerAsync(account.Id);
            Assert.True(ledgerResult.IsSuccess, ledgerResult.ErrorMessage);
            var ledger = ledgerResult.Value!;
            decimal calculatedBalance = ledger.Sum(e => e.Direction == "CREDIT" ? e.Amount : -e.Amount);
            Assert.Equal(finalAccount.Value.Balance, calculatedBalance);
        }
    }
}

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Sayra.Backend.Api;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.IntegrationTests
{
    public class Phase03ApiAndContractTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public Phase03ApiAndContractTests(WebApplicationFactory<Program> factory)
        {
            EnvLoader.Load();
            TestAdminSeeder.EnsureAdminUserCreatedAsync(factory).GetAwaiter().GetResult();
            _client = factory.CreateClient();
            _client.DefaultRequestHeaders.Add("X-User-Id", TestAdminSeeder.AdminUserId.ToString());
            _client.DefaultRequestHeaders.Add("X-User-Role", "Administrator");
        }

        [Fact]
        public async Task Auth_Login_And_Gamer_Authenticate_Endpoints_Should_Succeed_With_Valid_Credentials()
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            string username = $"user_{suffix}";
            string email = $"user_{suffix}@test.dev";
            string password = "StrongPassword123!";

            // 1. Create Gamer
            var createGamerResp = await _client.PostAsJsonAsync("/api/gamers", new
            {
                username = username,
                email = email,
                password = password
            });
            Assert.Equal(HttpStatusCode.Created, createGamerResp.StatusCode);

            // 2. Test POST /api/auth/login
            var loginResp = await _client.PostAsJsonAsync("/api/auth/login", new AuthenticateGamerRequestDto
            {
                UsernameOrEmail = username,
                Password = password
            });
            Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
            var loginData = await loginResp.Content.ReadFromJsonAsync<AuthenticateGamerResponseDto>();
            Assert.NotNull(loginData);
            Assert.True(loginData.IsSuccess);
            Assert.Equal(username, loginData.Username);

            // 3. Test POST /api/gamers/authenticate
            var gamerAuthResp = await _client.PostAsJsonAsync("/api/gamers/authenticate", new AuthenticateGamerRequestDto
            {
                UsernameOrEmail = email,
                Password = password
            });
            Assert.Equal(HttpStatusCode.OK, gamerAuthResp.StatusCode);
            var gamerAuthData = await gamerAuthResp.Content.ReadFromJsonAsync<AuthenticateGamerResponseDto>();
            Assert.NotNull(gamerAuthData);
            Assert.True(gamerAuthData.IsSuccess);
        }

        [Fact]
        public async Task Account_Deposit_Balance_And_Ledger_Endpoints_Should_Reflect_Transactions()
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);

            // 1. Create Gamer
            var createGamerResp = await _client.PostAsJsonAsync("/api/gamers", new
            {
                username = $"gamer_{suffix}",
                email = $"gamer_{suffix}@test.dev",
                password = "Password123!"
            });
            Assert.Equal(HttpStatusCode.Created, createGamerResp.StatusCode);
            var gamer = await createGamerResp.Content.ReadFromJsonAsync<GamerResponseDto>();
            Assert.NotNull(gamer);

            // 2. Deposit via POST /api/accounts/{gamerId}/deposit
            var depositResp = await _client.PostAsJsonAsync($"/api/accounts/{gamer.Id}/deposit", new CreditAccountRequestDto
            {
                Amount = 150.00m,
                Currency = "SAY",
                Reference = $"REF_{suffix}",
                EntryType = "DEPOSIT",
                Description = "Initial deposit for testing"
            });
            Assert.Equal(HttpStatusCode.OK, depositResp.StatusCode);

            // 3. GET /api/accounts/{gamerId}/balance
            var balResp = await _client.GetAsync($"/api/accounts/{gamer.Id}/balance");
            Assert.Equal(HttpStatusCode.OK, balResp.StatusCode);
            var bal = await balResp.Content.ReadFromJsonAsync<AccountBalanceResponseDto>();
            Assert.NotNull(bal);
            Assert.Equal(150.00m, bal.Balance);

            // 4. GET /api/accounts/{gamerId}/ledger
            var ledgerResp = await _client.GetAsync($"/api/accounts/{gamer.Id}/ledger");
            Assert.Equal(HttpStatusCode.OK, ledgerResp.StatusCode);
            var entries = await ledgerResp.Content.ReadFromJsonAsync<LedgerEntryResponseDto[]>();
            Assert.NotNull(entries);
            Assert.NotEmpty(entries);
            Assert.Equal(150.00m, entries[0].Amount);
        }

        [Fact]
        public async Task Payment_And_Financial_Transaction_Query_Endpoints_Should_Return_Expected_Data()
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);

            // 1. Create Gamer & Deposit
            var createGamerResp = await _client.PostAsJsonAsync("/api/gamers", new
            {
                username = $"payuser_{suffix}",
                email = $"payuser_{suffix}@test.dev",
                password = "Password123!"
            });
            Assert.Equal(HttpStatusCode.Created, createGamerResp.StatusCode);
            var gamer = await createGamerResp.Content.ReadFromJsonAsync<GamerResponseDto>();
            Assert.NotNull(gamer);

            // Get Gamer Account ID
            var accResp = await _client.GetAsync($"/api/gamers/{gamer.Id}/account");
            Assert.Equal(HttpStatusCode.OK, accResp.StatusCode);
            var accData = await accResp.Content.ReadFromJsonAsync<GamerAccountResponseDto>();
            Assert.NotNull(accData);
            Guid accountId = accData.Id;

            // Deposit 500
            await _client.PostAsJsonAsync($"/api/accounts/{gamer.Id}/deposit", new CreditAccountRequestDto
            {
                Amount = 500.00m,
                Currency = "SAY",
                Reference = $"DEP_{suffix}"
            });

            // 2. Create Payment POST /api/payments
            string idempotencyKey = $"IDEM_PAY_{suffix}";
            var payRequest = new HttpRequestMessage(HttpMethod.Post, "/api/payments")
            {
                Content = JsonContent.Create(new CreatePaymentRequestDto
                {
                    GamerAccountId = accountId,
                    Amount = 75.00m,
                    Currency = "SAY",
                    PaymentMethod = "ACCOUNT_BALANCE",
                    IdempotencyKey = idempotencyKey,
                    Reference = $"PAY_REF_{suffix}",
                    Description = "Store Purchase"
                })
            };
            payRequest.Headers.Add("Idempotency-Key", idempotencyKey);

            var payResp = await _client.SendAsync(payRequest);
            Assert.Equal(HttpStatusCode.Created, payResp.StatusCode);
            var payment = await payResp.Content.ReadFromJsonAsync<PaymentResponseDto>();
            Assert.NotNull(payment);
            Assert.Equal("COMPLETED", payment.Status);
            Assert.Equal(75.00m, payment.Amount);

            // 3. GET /api/payments/{id}
            var getPayResp = await _client.GetAsync($"/api/payments/{payment.Id}");
            Assert.Equal(HttpStatusCode.OK, getPayResp.StatusCode);

            // 4. GET /api/transactions/{id}
            Assert.NotNull(payment.FinancialTransactionId);
            var getTxResp = await _client.GetAsync($"/api/transactions/{payment.FinancialTransactionId.Value}");
            Assert.Equal(HttpStatusCode.OK, getTxResp.StatusCode);
            var tx = await getTxResp.Content.ReadFromJsonAsync<FinancialTransactionResponseDto>();
            Assert.NotNull(tx);
            Assert.Equal("COMPLETED", tx.Status);
        }
    }
}

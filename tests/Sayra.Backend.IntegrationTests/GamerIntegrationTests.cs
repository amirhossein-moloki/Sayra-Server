using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Sayra.Backend.Api;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.IntegrationTests
{
    public class GamerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public GamerIntegrationTests(WebApplicationFactory<Program> factory)
        {
            EnvLoader.Load();
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task Full_Gamer_Registration_Authentication_And_Profile_Lifecycle_Flow()
        {
            string uniqueKey = Guid.NewGuid().ToString("N").Substring(0, 8);
            string username = $"gamer_{uniqueKey}";
            string email = $"gamer_{uniqueKey}@example.com";
            string password = "StrongPassword123!";

            // 1. Register new Gamer
            var createRequest = new CreateGamerRequestDto
            {
                Username = username,
                Email = email,
                Password = password,
                FirstName = "Test",
                LastName = "Gamer",
                PhoneNumber = "+1234567890"
            };

            var createResponse = await _client.PostAsJsonAsync("/api/gamers", createRequest);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            var createdGamer = await createResponse.Content.ReadFromJsonAsync<GamerResponseDto>();
            Assert.NotNull(createdGamer);
            Assert.Equal(username, createdGamer.Username);
            Assert.Equal(email, createdGamer.Email);
            Assert.Equal("Active", createdGamer.Status);
            Assert.StartsWith("GMR-", createdGamer.GamerId);

            // 2. Fetch Gamer profile by ID
            var getGamerResponse = await _client.GetAsync($"/api/gamers/{createdGamer.Id}");
            Assert.Equal(HttpStatusCode.OK, getGamerResponse.StatusCode);
            var fetchedGamer = await getGamerResponse.Content.ReadFromJsonAsync<GamerResponseDto>();
            Assert.NotNull(fetchedGamer);
            Assert.Equal(createdGamer.Id, fetchedGamer.Id);

            // 3. Authenticate Gamer with valid credentials
            var authRequest = new AuthenticateGamerRequestDto
            {
                UsernameOrEmail = username,
                Password = password
            };

            var authResponse = await _client.PostAsJsonAsync("/api/gamers/authenticate", authRequest);
            Assert.Equal(HttpStatusCode.OK, authResponse.StatusCode);
            var authResult = await authResponse.Content.ReadFromJsonAsync<AuthenticateGamerResponseDto>();
            Assert.NotNull(authResult);
            Assert.True(authResult.IsSuccess);
            Assert.Equal(createdGamer.Id, authResult.GamerId);
            Assert.Equal(username, authResult.Username);
            Assert.False(string.IsNullOrWhiteSpace(authResult.AccountNumber));

            // 4. Query Gamer Account
            var getAccountResponse = await _client.GetAsync($"/api/gamers/{createdGamer.Id}/account");
            Assert.Equal(HttpStatusCode.OK, getAccountResponse.StatusCode);
            var gamerAccount = await getAccountResponse.Content.ReadFromJsonAsync<GamerAccountResponseDto>();
            Assert.NotNull(gamerAccount);
            Assert.Equal(createdGamer.Id, gamerAccount.GamerEntityId);
            Assert.Equal("Active", gamerAccount.Status);
            Assert.Equal("SAY", gamerAccount.Currency);

            // 5. Update Gamer profile
            var updateProfileRequest = new UpdateGamerProfileRequestDto
            {
                FirstName = "UpdatedFirst",
                LastName = "UpdatedLast"
            };

            var updateResponse = await _client.PutAsJsonAsync($"/api/gamers/{createdGamer.Id}/profile", updateProfileRequest);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            var updatedGamer = await updateResponse.Content.ReadFromJsonAsync<GamerResponseDto>();
            Assert.NotNull(updatedGamer);
            Assert.Equal("UpdatedFirst", updatedGamer.FirstName);
            Assert.Equal("UpdatedLast", updatedGamer.LastName);

            // 6. Change Password
            var changePasswordRequest = new ChangeGamerPasswordRequestDto
            {
                CurrentPassword = password,
                NewPassword = "NewStrongPassword456!"
            };

            var changePwResponse = await _client.PostAsJsonAsync($"/api/gamers/{createdGamer.Id}/change-password", changePasswordRequest);
            Assert.Equal(HttpStatusCode.OK, changePwResponse.StatusCode);

            // 7. Authenticate with new password
            var authNewRequest = new AuthenticateGamerRequestDto
            {
                UsernameOrEmail = email, // Testing authentication by email
                Password = "NewStrongPassword456!"
            };

            var authNewResponse = await _client.PostAsJsonAsync("/api/gamers/authenticate", authNewRequest);
            Assert.Equal(HttpStatusCode.OK, authNewResponse.StatusCode);
            var authNewResult = await authNewResponse.Content.ReadFromJsonAsync<AuthenticateGamerResponseDto>();
            Assert.NotNull(authNewResult);
            Assert.True(authNewResult.IsSuccess);

            // 8. Deactivate Gamer
            var deactivateResponse = await _client.PostAsJsonAsync($"/api/gamers/{createdGamer.Id}/deactivate", new { });
            Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);

            // 9. Authenticate deactivated Gamer -> should fail with ACCOUNT_DISABLED
            var authDeactivatedResponse = await _client.PostAsJsonAsync("/api/gamers/authenticate", authNewRequest);
            Assert.Equal(HttpStatusCode.Unauthorized, authDeactivatedResponse.StatusCode);
        }

        [Fact]
        public async Task CreateGamer_Duplicate_Username_And_Email_Should_Return_Conflict()
        {
            string uniqueKey = Guid.NewGuid().ToString("N").Substring(0, 8);
            string username = $"dupgamer_{uniqueKey}";
            string email = $"dupgamer_{uniqueKey}@example.com";

            var createRequest = new CreateGamerRequestDto
            {
                Username = username,
                Email = email,
                Password = "Password123!"
            };

            var firstResponse = await _client.PostAsJsonAsync("/api/gamers", createRequest);
            Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

            // Attempt duplicate username
            var duplicateUsernameRequest = new CreateGamerRequestDto
            {
                Username = username,
                Email = $"different_{uniqueKey}@example.com",
                Password = "Password123!"
            };
            var dupUsernameResponse = await _client.PostAsJsonAsync("/api/gamers", duplicateUsernameRequest);
            Assert.Equal(HttpStatusCode.Conflict, dupUsernameResponse.StatusCode);

            // Attempt duplicate email
            var duplicateEmailRequest = new CreateGamerRequestDto
            {
                Username = $"different_{uniqueKey}",
                Email = email,
                Password = "Password123!"
            };
            var dupEmailResponse = await _client.PostAsJsonAsync("/api/gamers", duplicateEmailRequest);
            Assert.Equal(HttpStatusCode.Conflict, dupEmailResponse.StatusCode);
        }
    }
}

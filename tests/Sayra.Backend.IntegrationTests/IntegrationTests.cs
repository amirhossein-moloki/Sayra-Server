using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Sayra.Backend.Api;
using Sayra.Backend.Api.Models;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Shared;

namespace Sayra.Backend.IntegrationTests
{
    public class IntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public IntegrationTests(WebApplicationFactory<Program> factory)
        {
            EnvLoader.Load();
            _factory = factory;
            _client = _factory.CreateClient();
        }

        [Fact]
        public async Task Health_Live_Endpoint_Should_Return_Success_Without_Dependencies()
        {
            // Act
            var response = await _client.GetAsync("/api/health/live");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var content = await response.Content.ReadAsStringAsync();
            using (var jsonDoc = JsonDocument.Parse(content))
            {
                var status = jsonDoc.RootElement.GetProperty("status").GetString();
                Assert.Equal("Healthy", status);
            }
        }

        [Fact]
        public void CryptographicService_Should_Encrypt_And_Decrypt_Aes_Correctly()
        {
            // Arrange
            var cryptoService = _factory.Services.GetService(typeof(ICryptographicService)) as ICryptographicService;
            Assert.NotNull(cryptoService);

            var plainText = System.Text.Encoding.UTF8.GetBytes("SuperSecretPayloadForSAYRA");
            var key = new byte[32]; // 256-bit key
            var iv = new byte[16];  // 128-bit IV
            Random.Shared.NextBytes(key);
            Random.Shared.NextBytes(iv);

            // Act
            var cipherText = cryptoService.EncryptAes256Cbc(plainText, key, iv);
            var decrypted = cryptoService.DecryptAes256Cbc(cipherText, key, iv);

            // Assert
            var decryptedString = System.Text.Encoding.UTF8.GetString(decrypted);
            Assert.Equal("SuperSecretPayloadForSAYRA", decryptedString);
        }

        [Fact]
        public void CryptographicService_Should_Compute_And_Verify_Hmac_Correctly()
        {
            // Arrange
            var cryptoService = _factory.Services.GetService(typeof(ICryptographicService)) as ICryptographicService;
            Assert.NotNull(cryptoService);

            var data = System.Text.Encoding.UTF8.GetBytes("ThePayloadToProtect");
            var key = System.Text.Encoding.UTF8.GetBytes("SecretHmacKey");

            // Act
            var hash = cryptoService.ComputeHmacSha256(data, key);
            var isVerified = cryptoService.VerifyHmacSha256(data, key, hash);

            // Assert
            Assert.True(isVerified);
        }
    }
}

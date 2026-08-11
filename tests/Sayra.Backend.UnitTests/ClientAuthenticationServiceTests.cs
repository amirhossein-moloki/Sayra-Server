using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Workstations;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Infrastructure.Security;

namespace Sayra.Backend.UnitTests
{
    public class ClientAuthenticationServiceTests
    {
        private readonly Mock<ICryptographicService> _cryptoMock;
        private readonly Mock<IConfiguration> _configMock;
        private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
        private readonly Mock<IServiceProvider> _serviceProviderMock;
        private readonly Mock<ILogger<ClientAuthenticationService>> _loggerMock;
        private readonly Mock<ITcpConnection> _connMock;

        private readonly Mock<ICommandHandler<AuthorizeWorkstationCommand, Workstation>> _authorizeMock;
        private readonly Mock<ICommandHandler<BindWorkstationConnectionCommand, Workstation>> _bindMock;

        private readonly string _masterKey = "MasterKey32BytesLongForHandshake!";
        private readonly ClientAuthenticationService _service;

        public ClientAuthenticationServiceTests()
        {
            _cryptoMock = new Mock<ICryptographicService>();
            _configMock = new Mock<IConfiguration>();
            _scopeFactoryMock = new Mock<IServiceScopeFactory>();
            _serviceProviderMock = new Mock<IServiceProvider>();
            _loggerMock = new Mock<ILogger<ClientAuthenticationService>>();
            _connMock = new Mock<ITcpConnection>();

            _authorizeMock = new Mock<ICommandHandler<AuthorizeWorkstationCommand, Workstation>>();
            _bindMock = new Mock<ICommandHandler<BindWorkstationConnectionCommand, Workstation>>();

            // Setup DI Resolution for scoped handlers
            var scopeMock = new Mock<IServiceScope>();
            scopeMock.Setup(s => s.ServiceProvider).Returns(_serviceProviderMock.Object);
            _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

            _serviceProviderMock.Setup(p => p.GetService(typeof(ICommandHandler<AuthorizeWorkstationCommand, Workstation>)))
                .Returns(_authorizeMock.Object);
            _serviceProviderMock.Setup(p => p.GetService(typeof(ICommandHandler<BindWorkstationConnectionCommand, Workstation>)))
                .Returns(_bindMock.Object);

            _configMock.Setup(c => c["SAYRA_MASTER_KEY"]).Returns(_masterKey);

            _connMock.SetupGet(c => c.ConnectionId).Returns("conn-id-123");

            _service = new ClientAuthenticationService(
                _cryptoMock.Object,
                _configMock.Object,
                _scopeFactoryMock.Object,
                _loggerMock.Object);
        }

        [Fact]
        public async Task GenerateChallengeAsync_Should_Create_Cryptographically_Secure_Challenge()
        {
            // Act
            string challenge = await _service.GenerateChallengeAsync(_connMock.Object);

            // Assert
            Assert.NotEmpty(challenge);
            byte[] bytes = Convert.FromBase64String(challenge);
            Assert.Equal(32, bytes.Length); // 32 bytes = 256 bits secure random
            _connMock.Verify(c => c.UpdateState(ConnectionLifecycleState.Authenticating), Times.Once);
        }

        [Fact]
        public async Task ValidateResponseAsync_With_Valid_Inputs_Should_Succeed()
        {
            // Arrange
            string challenge = await _service.GenerateChallengeAsync(_connMock.Object);

            byte[] masterKeyBytes = Encoding.UTF8.GetBytes(_masterKey);
            byte[] challengeStringToSign = Encoding.UTF8.GetBytes(challenge);
            byte[] expectedHmac = new byte[32];
            RandomNumberGenerator.Fill(expectedHmac);

            _cryptoMock.Setup(c => c.ComputeHmacSha256(It.Is<byte[]>(b => b.Length == challengeStringToSign.Length), It.Is<byte[]>(k => k.Length == masterKeyBytes.Length)))
                .Returns(expectedHmac);

            byte[] sessionKey = RandomNumberGenerator.GetBytes(32);
            byte[] iv = RandomNumberGenerator.GetBytes(16);
            byte[] aesKey = SHA256.HashData(masterKeyBytes);
            byte[] encryptedSessionKey = new byte[32]; // simulated encrypted key

            _cryptoMock.Setup(c => c.DecryptAes256Cbc(encryptedSessionKey, It.IsAny<byte[]>(), iv))
                .Returns(sessionKey);

            var responseDto = new AuthResponseDto
            {
                Hmac = Convert.ToBase64String(expectedHmac),
                EncryptedSessionKey = Convert.ToBase64String(encryptedSessionKey),
                Iv = Convert.ToBase64String(iv),
                PcId = "PC-01",
                Hostname = "DESKTOP-01"
            };

            // Workstation authorization
            var workstation = new Workstation { PcId = "PC-01", Status = "Offline" };
            var authResult = Shared.Result<Workstation>.Success(workstation);
            _authorizeMock.Setup(a => a.HandleAsync(It.IsAny<AuthorizeWorkstationCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(authResult);

            var bindResult = Shared.Result<Workstation>.Success(workstation);
            _bindMock.Setup(b => b.HandleAsync(It.IsAny<BindWorkstationConnectionCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(bindResult);

            // Act
            var result = await _service.ValidateResponseAsync(_connMock.Object, responseDto);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(sessionKey, result.SessionKey);
            Assert.Equal(ConnectionLifecycleState.Authenticated, result.NewState);

            _connMock.VerifySet(c => c.SessionKey = sessionKey, Times.Once);
            _connMock.Verify(c => c.UpdateState(ConnectionLifecycleState.Authenticated), Times.Once);
        }

        [Fact]
        public async Task ValidateResponseAsync_With_Invalid_HMAC_Should_Fail()
        {
            // Arrange
            string challenge = await _service.GenerateChallengeAsync(_connMock.Object);

            byte[] masterKeyBytes = Encoding.UTF8.GetBytes(_masterKey);
            byte[] challengeStringToSign = Encoding.UTF8.GetBytes(challenge);
            byte[] expectedHmac = new byte[32];
            RandomNumberGenerator.Fill(expectedHmac);

            _cryptoMock.Setup(c => c.ComputeHmacSha256(It.IsAny<byte[]>(), It.IsAny<byte[]>()))
                .Returns(expectedHmac);

            byte[] invalidHmac = new byte[32]; // Tampered / mismatched HMAC

            var responseDto = new AuthResponseDto
            {
                Hmac = Convert.ToBase64String(invalidHmac),
                EncryptedSessionKey = Convert.ToBase64String(new byte[16]),
                Iv = Convert.ToBase64String(new byte[16]),
                PcId = "PC-01"
            };

            // Act
            var result = await _service.ValidateResponseAsync(_connMock.Object, responseDto);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("AUTH_FAILED", result.ErrorCode);
            Assert.Contains("HMAC challenge response verification failed", result.ErrorMessage);
        }

        [Fact]
        public async Task ValidateResponseAsync_With_Invalid_SessionKey_Length_Should_Fail()
        {
            // Arrange
            string challenge = await _service.GenerateChallengeAsync(_connMock.Object);

            byte[] masterKeyBytes = Encoding.UTF8.GetBytes(_masterKey);
            byte[] expectedHmac = new byte[32];
            RandomNumberGenerator.Fill(expectedHmac);

            _cryptoMock.Setup(c => c.ComputeHmacSha256(It.IsAny<byte[]>(), It.IsAny<byte[]>()))
                .Returns(expectedHmac);

            byte[] invalidShortKey = new byte[16]; // Expected is 32 bytes
            byte[] iv = RandomNumberGenerator.GetBytes(16);

            _cryptoMock.Setup(c => c.DecryptAes256Cbc(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>()))
                .Returns(invalidShortKey);

            var responseDto = new AuthResponseDto
            {
                Hmac = Convert.ToBase64String(expectedHmac),
                EncryptedSessionKey = Convert.ToBase64String(new byte[16]),
                Iv = Convert.ToBase64String(iv),
                PcId = "PC-01"
            };

            // Act
            var result = await _service.ValidateResponseAsync(_connMock.Object, responseDto);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("AUTH_FAILED", result.ErrorCode);
            Assert.Contains("Invalid SessionKey length", result.ErrorMessage);
        }

        [Fact]
        public async Task GenerateChallengeAsync_When_BruteForce_Limit_Reached_Should_Throw_AuthenticationException()
        {
            // Arrange
            // Perform multiple failed validations to reach limit (limit is 3)
            var responseDto = new AuthResponseDto
            {
                Hmac = Convert.ToBase64String(new byte[32]),
                EncryptedSessionKey = Convert.ToBase64String(new byte[32]),
                Iv = Convert.ToBase64String(new byte[16]),
                PcId = "PC-01"
            };

            for (int i = 0; i < 3; i++)
            {
                await _service.GenerateChallengeAsync(_connMock.Object);
                await _service.ValidateResponseAsync(_connMock.Object, responseDto);
            }

            // Act & Assert
            var ex = await Assert.ThrowsAsync<AuthenticationException>(() => _service.GenerateChallengeAsync(_connMock.Object));
            Assert.Equal("AUTH_FAILED", ex.ErrorCode);
            Assert.Contains("Too many failed authentication attempts", ex.Message);
        }
    }
}

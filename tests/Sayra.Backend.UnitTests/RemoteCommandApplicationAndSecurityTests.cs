using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Communication;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Infrastructure.Transport;
using Sayra.Backend.Shared;
using Moq;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class RemoteCommandApplicationAndSecurityTests
    {
        private readonly DbContextOptions<ApplicationDbContext> _dbOptions;

        public RemoteCommandApplicationAndSecurityTests()
        {
            _dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: $"RemoteCmdTests_{Guid.NewGuid():N}")
                .Options;
        }

        private (ServiceProvider provider, ApplicationDbContext dbContext, Mock<ITcpConnectionRegistry> mockRegistry, Mock<ISecureMessageService> mockSecureMsg, Mock<IRedisService> mockRedis, Mock<IAuthorizationService> mockAuthService) SetupDependencies()
        {
            var services = new ServiceCollection();
            var dbContext = new ApplicationDbContext(_dbOptions);
            services.AddSingleton(dbContext);

            var mockRegistry = new Mock<ITcpConnectionRegistry>();
            var mockSecureMsg = new Mock<ISecureMessageService>();
            var mockRedis = new Mock<IRedisService>();
            var mockSecurityEvents = new Mock<ISecurityEventService>();
            var mockAuthService = new Mock<IAuthorizationService>();

            services.AddSingleton(mockRegistry.Object);
            services.AddSingleton(mockSecureMsg.Object);
            services.AddSingleton(mockRedis.Object);
            services.AddSingleton(mockSecurityEvents.Object);
            services.AddSingleton(mockAuthService.Object);

            var serviceProvider = services.BuildServiceProvider();

            var scopeFactoryMock = new Mock<IServiceScopeFactory>();
            scopeFactoryMock.Setup(s => s.CreateScope()).Returns(() =>
            {
                var scopeMock = new Mock<IServiceScope>();
                scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProvider);
                return scopeMock.Object;
            });

            var manager = new RemoteCommandManager(
                scopeFactoryMock.Object,
                mockRegistry.Object,
                mockSecureMsg.Object,
                mockRedis.Object,
                NullLogger<RemoteCommandManager>.Instance);

            services.AddSingleton<IRemoteCommandManager>(manager);

            return (services.BuildServiceProvider(), dbContext, mockRegistry, mockSecureMsg, mockRedis, mockAuthService);
        }

        [Fact]
        public async Task CreateAndDispatchCommandAsync_UnauthorizedCaller_ShouldReturnForbiddenResult()
        {
            // Arrange
            var (provider, dbContext, _, _, _, mockAuthService) = SetupDependencies();
            var manager = provider.GetRequiredService<IRemoteCommandManager>();

            var workstation = new Workstation
            {
                PcId = "PC-AUTH-DENIED",
                Name = "Auth Workstation",
                SiteId = "SITE-MAIN",
                Hostname = "HOST-AUTH",
                IpAddress = "192.168.1.52",
                MacAddress = "00:11:22:33:44:57",
                Status = "ONLINE"
            };
            dbContext.Workstations.Add(workstation);
            await dbContext.SaveChangesAsync();

            var unprivilegedUser = new UserPrincipal
            {
                UserId = Guid.NewGuid(),
                Username = "UnauthorizedUser",
                IsAuthenticated = true,
                Permissions = new List<string>()
            };

            mockAuthService.Setup(a => a.AuthorizeAsync(unprivilegedUser, PermissionCatalog.ControlWorkstations, It.IsAny<Workstation>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AuthorizationResult.Denied("Missing ControlWorkstations permission"));

            var request = new CreateRemoteCommandRequestDto
            {
                CommandType = "RESTART_WORKSTATION",
                TargetWorkstationId = workstation.Id,
                TargetPcId = workstation.PcId,
                RequestedBy = unprivilegedUser.Username,
                CallerPrincipal = unprivilegedUser
            };

            // Act
            var result = await manager.CreateAndDispatchCommandAsync(request);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("FORBIDDEN", result.ErrorCode);
        }

        [Fact]
        public async Task CreateAndDispatchCommandAsync_OnlineWorkstation_ShouldCreate_Dispatch_AndTransitionToDelivered()
        {
            // Arrange
            var (provider, dbContext, mockRegistry, mockSecureMsg, mockRedis, _) = SetupDependencies();
            var manager = provider.GetRequiredService<IRemoteCommandManager>();

            var workstation = new Workstation
            {
                PcId = "PC-UNIT-01",
                Name = "Unit Workstation 01",
                SiteId = "SITE-MAIN",
                Hostname = "HOST-01",
                IpAddress = "192.168.1.50",
                MacAddress = "00:11:22:33:44:55",
                Status = "ONLINE"
            };
            dbContext.Workstations.Add(workstation);
            await dbContext.SaveChangesAsync();

            var mockConnection = new Mock<ITcpConnection>();
            mockConnection.Setup(c => c.ConnectionId).Returns("CONN-100");
            mockConnection.Setup(c => c.PcId).Returns("PC-UNIT-01");
            mockConnection.Setup(c => c.State).Returns(ConnectionLifecycleState.Active);
            mockConnection.Setup(c => c.SessionKey).Returns(new byte[32]);

            mockRegistry.Setup(r => r.GetByPcId("PC-UNIT-01")).Returns(mockConnection.Object);
            mockSecureMsg.Setup(s => s.SendSecureMessageAsync(It.IsAny<ConnectionSession>(), It.IsAny<CommandMessage<string>>()))
                .Returns(Task.CompletedTask);

            var request = new CreateRemoteCommandRequestDto
            {
                CommandType = "LOCK_WORKSTATION",
                TargetWorkstationId = workstation.Id,
                TargetPcId = workstation.PcId,
                RequestedBy = "ManagerUser",
                Payload = "{\"reason\":\"Screen Lock\"}"
            };

            // Act
            var result = await manager.CreateAndDispatchCommandAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("DELIVERED", result.Value.Status);

            var dbCommand = await dbContext.RemoteCommands.FirstOrDefaultAsync(c => c.CommandId == result.Value.CommandId);
            Assert.NotNull(dbCommand);
            Assert.Equal("DELIVERED", dbCommand.Status);
            Assert.Equal("CONN-100", dbCommand.TargetConnectionId);
        }

        [Fact]
        public async Task CreateAndDispatchCommandAsync_OfflineWorkstation_ShouldQueueCommand()
        {
            // Arrange
            var (provider, dbContext, mockRegistry, mockSecureMsg, _, _) = SetupDependencies();
            var manager = provider.GetRequiredService<IRemoteCommandManager>();

            var workstation = new Workstation
            {
                PcId = "PC-OFFLINE-01",
                Name = "Offline Workstation",
                SiteId = "SITE-MAIN",
                Hostname = "HOST-OFF",
                IpAddress = "192.168.1.51",
                MacAddress = "00:11:22:33:44:56",
                Status = "OFFLINE"
            };
            dbContext.Workstations.Add(workstation);
            await dbContext.SaveChangesAsync();

            mockRegistry.Setup(r => r.GetByPcId("PC-OFFLINE-01")).Returns((ITcpConnection?)null);

            var request = new CreateRemoteCommandRequestDto
            {
                CommandType = "PING",
                TargetWorkstationId = workstation.Id,
                TargetPcId = workstation.PcId,
                RequestedBy = "Admin"
            };

            // Act
            var result = await manager.CreateAndDispatchCommandAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("QUEUED", result.Value!.Status);

            var dbCmd = await dbContext.RemoteCommands.FirstOrDefaultAsync(c => c.CommandId == result.Value.CommandId);
            Assert.Equal("QUEUED", dbCmd!.Status);
            mockSecureMsg.Verify(s => s.SendSecureMessageAsync(It.IsAny<ConnectionSession>(), It.IsAny<CommandMessage<string>>()), Times.Never);
        }

        [Fact]
        public async Task ProcessCommandAckAsync_CrossWorkstationInjection_ShouldBeRejected()
        {
            // Arrange
            var (provider, dbContext, _, _, _, _) = SetupDependencies();
            var manager = provider.GetRequiredService<IRemoteCommandManager>();

            var command = RemoteCommand.Create("CMD-FORGERY-01", "LOCK_WORKSTATION", Guid.NewGuid(), "PC-ALPHA", "Admin");
            dbContext.RemoteCommands.Add(command);
            await dbContext.SaveChangesAsync();

            // Act: Attacker connection on PC-BETA attempts to send ACK for PC-ALPHA's command
            var ackResult = await manager.ProcessCommandAckAsync("CMD-FORGERY-01", "PC-BETA", "ACKNOWLEDGED", null);

            // Assert
            Assert.False(ackResult.IsSuccess);
            Assert.Equal("CROSS_WORKSTATION_FORGERY", ackResult.ErrorCode);

            var dbCmd = await dbContext.RemoteCommands.FirstOrDefaultAsync(c => c.CommandId == "CMD-FORGERY-01");
            Assert.Equal("CREATED", dbCmd!.Status);
        }

        [Fact]
        public async Task ProcessCommandResultAsync_TimeoutVsSuccessRace_FirstTerminalWins()
        {
            // Arrange
            var (provider, dbContext, _, _, _, _) = SetupDependencies();
            var manager = provider.GetRequiredService<IRemoteCommandManager>();

            var command = RemoteCommand.Create("CMD-RACE-01", "RESTART_WORKSTATION", Guid.NewGuid(), "PC-RACE", "Admin");
            command.TransitionTo("SENDING");
            command.TransitionTo("EXECUTION_TIMEOUT"); // Already terminal via timeout
            dbContext.RemoteCommands.Add(command);
            await dbContext.SaveChangesAsync();

            // Act: Late success result arrives from client
            var result = await manager.ProcessCommandResultAsync("CMD-RACE-01", "PC-RACE", "Executed", "Restart completed", null, null);

            // Assert: Operation succeeds safely without corrupting status out of EXECUTION_TIMEOUT
            Assert.True(result.IsSuccess);

            var dbCmd = await dbContext.RemoteCommands.FirstOrDefaultAsync(c => c.CommandId == "CMD-RACE-01");
            Assert.Equal("EXECUTION_TIMEOUT", dbCmd!.Status);
        }
    }
}

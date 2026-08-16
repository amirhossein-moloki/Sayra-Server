using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Gamers;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Application.Workstations;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Caching;
using Sayra.Backend.Infrastructure.Transport;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Diagnostics;
using Sayra.Backend.Infrastructure.Logging;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Infrastructure.Security;

namespace Sayra.Backend.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            // 1. Register Strongly-Typed Options Sections
            services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
            services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
            services.Configure<ServerOptions>(configuration.GetSection(ServerOptions.SectionName));
            services.Configure<DiscoveryOptions>(configuration.GetSection(DiscoveryOptions.SectionName));
            services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
            services.Configure<TlsOptions>(configuration.GetSection(TlsOptions.SectionName));
            services.Configure<LoggingOptions>(configuration.GetSection(LoggingOptions.SectionName));
            services.Configure<UpdatesOptions>(configuration.GetSection(UpdatesOptions.SectionName));
            services.Configure<TelemetryOptions>(configuration.GetSection(TelemetryOptions.SectionName));

            // 2. Database Foundation Setup
            var dbOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
            var dbConnectionString = dbOptions.ConnectionString;

            if (string.IsNullOrWhiteSpace(dbConnectionString))
            {
                throw new System.InvalidOperationException(
                    "Critical database connection string is missing or empty. " +
                    "Please configure 'Database:ConnectionString' via environment variables, " +
                    "local secrets, or configuration files.");
            }

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseNpgsql(dbConnectionString, npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(3);
                });
            });

            // Register database abstractions
            services.AddScoped<DbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
            services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<ApplicationDbContext>());
            services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

            // 3. Redis Foundation Setup
            var redisOptions = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();
            var redisConnectionString = redisOptions.ConnectionString;
            if (string.IsNullOrEmpty(redisConnectionString))
            {
                throw new System.InvalidOperationException(
                    "Critical Redis connection string is missing or empty. " +
                    "Please configure 'Redis:ConnectionString' via environment variables, " +
                    "local secrets, or configuration files.");
            }

            try
            {
                var redisConfig = ConfigurationOptions.Parse(redisConnectionString);
                redisConfig.AbortOnConnectFail = false; // Prevents crash on startup if offline

                services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConfig));
                services.AddSingleton<IRedisService, RedisService>();
            }
            catch
            {
                // Setup safe fallback or log using the configured connection string
                var lazyMultiplexer = new Lazy<IConnectionMultiplexer>(() =>
                    ConnectionMultiplexer.Connect(new ConfigurationOptions { EndPoints = { redisConnectionString }, AbortOnConnectFail = false }));
                services.AddSingleton<IConnectionMultiplexer>(_ => lazyMultiplexer.Value);
                services.AddSingleton<IRedisService, RedisService>();
            }

            // 4. Security & Cryptographic abstractions
            services.AddSingleton<ICryptographicService, CryptographicService>();
            services.AddSingleton<IClientAuthenticationService, ClientAuthenticationService>();
            services.AddSingleton<ITcpAuthenticationService, TcpAuthenticationService>();
            services.AddSingleton<ISecureMessageService, SecureMessageService>();
            services.AddSingleton<IPasswordHasher, PasswordHasher>();

            // 5. Command & Query Handlers
            services.AddScoped<ICommandHandler<RegisterWorkstationCommand, Workstation>, RegisterWorkstationCommandHandler>();
            services.AddScoped<IQueryHandler<GetWorkstationByPcIdQuery, Workstation?>, GetWorkstationByPcIdQueryHandler>();
            services.AddScoped<ICommandHandler<AuthorizeWorkstationCommand, Workstation>, AuthorizeWorkstationCommandHandler>();
            services.AddScoped<ICommandHandler<BindWorkstationConnectionCommand, Workstation>, BindWorkstationConnectionCommandHandler>();
            services.AddScoped<ICommandHandler<UnbindWorkstationConnectionCommand, Workstation?>, UnbindWorkstationConnectionCommandHandler>();

            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Organizations.CreateOrganizationCommand, Organization>, Sayra.Backend.Application.Organizations.CreateOrganizationCommandHandler>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Organizations.DeactivateOrganizationCommand, Organization>, Sayra.Backend.Application.Organizations.DeactivateOrganizationCommandHandler>();
            services.AddScoped<IQueryHandler<Sayra.Backend.Application.Organizations.GetOrganizationQuery, Organization>, Sayra.Backend.Application.Organizations.GetOrganizationQueryHandler>();

            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Locations.CreateSiteCommand, Site>, Sayra.Backend.Application.Locations.CreateSiteCommandHandler>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Locations.DeactivateSiteCommand, Site>, Sayra.Backend.Application.Locations.DeactivateSiteCommandHandler>();
            services.AddScoped<IQueryHandler<Sayra.Backend.Application.Locations.GetSiteQuery, Site>, Sayra.Backend.Application.Locations.GetSiteQueryHandler>();

            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Locations.CreateZoneCommand, Zone>, Sayra.Backend.Application.Locations.CreateZoneCommandHandler>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Locations.DeactivateZoneCommand, Zone>, Sayra.Backend.Application.Locations.DeactivateZoneCommandHandler>();
            services.AddScoped<IQueryHandler<Sayra.Backend.Application.Locations.GetZoneQuery, Zone>, Sayra.Backend.Application.Locations.GetZoneQueryHandler>();

            services.AddScoped<ICommandHandler<AssignWorkstationCommand, Sayra.Backend.Contracts.WorkstationAssignmentResponseDto>, AssignWorkstationCommandHandler>();
            services.AddScoped<IQueryHandler<GetWorkstationAssignmentQuery, Sayra.Backend.Contracts.WorkstationAssignmentResponseDto>, GetWorkstationAssignmentQueryHandler>();

            // Gamer, GamerCredential & GamerAccount Handlers
            services.AddScoped<ICommandHandler<CreateGamerCommand, Gamer>, CreateGamerCommandHandler>();
            services.AddScoped<ICommandHandler<UpdateGamerProfileCommand, Gamer>, UpdateGamerProfileCommandHandler>();
            services.AddScoped<ICommandHandler<DeactivateGamerCommand, Gamer>, DeactivateGamerCommandHandler>();
            services.AddScoped<ICommandHandler<ChangeGamerPasswordCommand, bool>, ChangeGamerPasswordCommandHandler>();
            services.AddScoped<ICommandHandler<AuthenticateGamerCommand, AuthenticateGamerResponseDto>, AuthenticateGamerCommandHandler>();
            services.AddScoped<IQueryHandler<GetGamerQuery, Gamer>, GetGamerQueryHandler>();
            services.AddScoped<IQueryHandler<GetGamerAccountQuery, GamerAccount>, GetGamerAccountQueryHandler>();

            // Reservation Handlers & Validation Service
            services.AddScoped<Sayra.Backend.Application.Reservations.IReservationValidationService, Sayra.Backend.Application.Reservations.ReservationValidationService>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Reservations.CreateReservationCommand, ReservationResponseDto>, Sayra.Backend.Application.Reservations.CreateReservationCommandHandler>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Reservations.ConfirmReservationCommand, ReservationResponseDto>, Sayra.Backend.Application.Reservations.ConfirmReservationCommandHandler>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Reservations.CancelReservationCommand, ReservationResponseDto>, Sayra.Backend.Application.Reservations.CancelReservationCommandHandler>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Reservations.ActivateReservationCommand, ReservationResponseDto>, Sayra.Backend.Application.Reservations.ActivateReservationCommandHandler>();
            services.AddScoped<IQueryHandler<Sayra.Backend.Application.Reservations.GetReservationQuery, ReservationResponseDto>, Sayra.Backend.Application.Reservations.GetReservationQueryHandler>();
            services.AddScoped<IQueryHandler<Sayra.Backend.Application.Reservations.ValidateReservationQuery, ReservationValidationResultDto>, Sayra.Backend.Application.Reservations.ValidateReservationQueryHandler>();

            // 6. TCP & UDP Transport Services
            services.AddSingleton<ITcpConnectionRegistry, TcpConnectionRegistry>();

            services.AddSingleton<TcpServer>();
            services.AddSingleton<ITcpServer>(provider => provider.GetRequiredService<TcpServer>());
            services.AddHostedService(provider => provider.GetRequiredService<TcpServer>());

            services.AddSingleton<UdpDiscoveryServer>();
            services.AddSingleton<IUdpDiscoveryServer>(provider => provider.GetRequiredService<UdpDiscoveryServer>());
            services.AddHostedService(provider => provider.GetRequiredService<UdpDiscoveryServer>());

            // 7. Health Checks
            services.AddHealthChecks()
                .AddDbContextCheck<ApplicationDbContext>(
                    name: "PostgreSQL",
                    failureStatus: HealthStatus.Unhealthy,
                    tags: new[] { "ready" })
                .AddCheck<RedisHealthCheck>(
                    name: "Redis",
                    failureStatus: HealthStatus.Unhealthy,
                    tags: new[] { "ready" });

            return services;
        }
    }
}

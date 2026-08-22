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
using Sayra.Backend.Application.Financial;
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
            services.AddScoped<IAuthorizationService, AuthorizationService>();

            // 5. Command & Query Handlers
            services.AddScoped<ICommandHandler<AssignRoleToUserCommand, bool>, RbacHandlers>();
            services.AddScoped<ICommandHandler<RemoveRoleFromUserCommand, bool>, RbacHandlers>();
            services.AddScoped<ICommandHandler<AssignPermissionToRoleCommand, bool>, RbacHandlers>();
            services.AddScoped<ICommandHandler<RemovePermissionFromRoleCommand, bool>, RbacHandlers>();
            services.AddScoped<IQueryHandler<GetUserPermissionsQuery, System.Collections.Generic.List<string>>, RbacHandlers>();
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

            // Financial Account & Ledger Foundation Services
            services.AddScoped<IFinancialAccountService, FinancialAccountService>();
            services.AddScoped<IFinancialTransactionService, FinancialTransactionService>();
            services.AddScoped<ICommandHandler<CreditAccountCommand, LedgerEntryResponseDto>, CreditAccountCommandHandler>();
            services.AddScoped<ICommandHandler<DebitAccountCommand, LedgerEntryResponseDto>, DebitAccountCommandHandler>();
            services.AddScoped<IQueryHandler<GetAccountBalanceQuery, AccountBalanceResponseDto>, GetAccountBalanceQueryHandler>();
            services.AddScoped<IQueryHandler<GetAccountLedgerQuery, System.Collections.Generic.IReadOnlyList<LedgerEntryResponseDto>>, GetAccountLedgerQueryHandler>();
            services.AddScoped<ICommandHandler<ProcessFinancialTransactionCommand, FinancialTransactionResponseDto>, ProcessFinancialTransactionCommandHandler>();
            services.AddScoped<ICommandHandler<ReverseFinancialTransactionCommand, FinancialTransactionResponseDto>, ReverseFinancialTransactionCommandHandler>();
            services.AddScoped<ICommandHandler<CreatePaymentCommand, PaymentResponseDto>, CreatePaymentCommandHandler>();
            services.AddScoped<IQueryHandler<GetFinancialTransactionQuery, FinancialTransactionResponseDto>, GetFinancialTransactionQueryHandler>();
            services.AddScoped<IQueryHandler<GetPaymentQuery, PaymentResponseDto>, GetPaymentQueryHandler>();
            services.AddScoped<IQueryHandler<GetTransactionByIdempotencyKeyQuery, FinancialTransactionResponseDto>, GetTransactionByIdempotencyKeyQueryHandler>();

            // Reservation Handlers & Validation Service
            services.AddScoped<Sayra.Backend.Application.Reservations.IReservationValidationService, Sayra.Backend.Application.Reservations.ReservationValidationService>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Reservations.CreateReservationCommand, ReservationResponseDto>, Sayra.Backend.Application.Reservations.CreateReservationCommandHandler>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Reservations.ConfirmReservationCommand, ReservationResponseDto>, Sayra.Backend.Application.Reservations.ConfirmReservationCommandHandler>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Reservations.CancelReservationCommand, ReservationResponseDto>, Sayra.Backend.Application.Reservations.CancelReservationCommandHandler>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Reservations.ActivateReservationCommand, ReservationResponseDto>, Sayra.Backend.Application.Reservations.ActivateReservationCommandHandler>();
            services.AddScoped<IQueryHandler<Sayra.Backend.Application.Reservations.GetReservationQuery, ReservationResponseDto>, Sayra.Backend.Application.Reservations.GetReservationQueryHandler>();
            services.AddScoped<IQueryHandler<Sayra.Backend.Application.Reservations.ValidateReservationQuery, ReservationValidationResultDto>, Sayra.Backend.Application.Reservations.ValidateReservationQueryHandler>();

            // Session Handlers & Domain Service
            services.AddScoped<Sayra.Backend.Application.Sessions.ISessionStateTransitionService, Sayra.Backend.Application.Sessions.SessionStateTransitionService>();
            services.AddSingleton<Sayra.Backend.Application.Sessions.ISessionTimeCalculator, Sayra.Backend.Application.Sessions.SessionTimeCalculator>();
            services.AddScoped<Sayra.Backend.Application.Sessions.ISessionExpirationService, Sayra.Backend.Application.Sessions.SessionExpirationService>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Sessions.StartSessionCommand, SessionResponseDto>, Sayra.Backend.Application.Sessions.StartSessionCommandHandler>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Sessions.PauseSessionCommand, SessionResponseDto>, Sayra.Backend.Application.Sessions.PauseSessionCommandHandler>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Sessions.ResumeSessionCommand, SessionResponseDto>, Sayra.Backend.Application.Sessions.ResumeSessionCommandHandler>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Sessions.StopSessionCommand, SessionResponseDto>, Sayra.Backend.Application.Sessions.StopSessionCommandHandler>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Sessions.CancelSessionCommand, SessionResponseDto>, Sayra.Backend.Application.Sessions.CancelSessionCommandHandler>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Sessions.TerminateSessionCommand, SessionResponseDto>, Sayra.Backend.Application.Sessions.TerminateSessionCommandHandler>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Sessions.ExtendSessionCommand, SessionExtensionResponseDto>, Sayra.Backend.Application.Sessions.ExtendSessionCommandHandler>();
            services.AddScoped<IQueryHandler<Sayra.Backend.Application.Sessions.GetSessionQuery, SessionResponseDto>, Sayra.Backend.Application.Sessions.GetSessionQueryHandler>();
            services.AddScoped<IQueryHandler<Sayra.Backend.Application.Sessions.GetSessionCurrentStateQuery, SessionResponseDto>, Sayra.Backend.Application.Sessions.GetSessionCurrentStateQueryHandler>();
            services.AddScoped<IQueryHandler<Sayra.Backend.Application.Sessions.GetSessionTimingQuery, SessionTimingResponseDto>, Sayra.Backend.Application.Sessions.GetSessionTimingQueryHandler>();
            services.AddScoped<IQueryHandler<Sayra.Backend.Application.Sessions.GetSessionDurationQuery, System.TimeSpan>, Sayra.Backend.Application.Sessions.GetSessionDurationQueryHandler>();
            services.AddScoped<IQueryHandler<Sayra.Backend.Application.Sessions.GetSessionRemainingTimeQuery, System.TimeSpan?>, Sayra.Backend.Application.Sessions.GetSessionRemainingTimeQueryHandler>();
            services.AddScoped<IQueryHandler<Sayra.Backend.Application.Sessions.GetActiveSessionByWorkstationQuery, SessionResponseDto?>, Sayra.Backend.Application.Sessions.GetActiveSessionByWorkstationQueryHandler>();
            services.AddScoped<IQueryHandler<Sayra.Backend.Application.Sessions.GetActiveSessionByGamerQuery, SessionResponseDto?>, Sayra.Backend.Application.Sessions.GetActiveSessionByGamerQueryHandler>();

            // Pricing Domain Services & Handlers
            services.AddScoped<Sayra.Backend.Application.Pricing.IRateResolver, Sayra.Backend.Application.Pricing.RateResolver>();
            services.AddScoped<Sayra.Backend.Application.Pricing.IRateSnapshotService, Sayra.Backend.Application.Pricing.RateSnapshotService>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Pricing.CreatePricingPlanCommand, PricingPlanResponseDto>, Sayra.Backend.Application.Pricing.CreatePricingPlanCommandHandler>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Pricing.CreatePricingRuleCommand, PricingRuleResponseDto>, Sayra.Backend.Application.Pricing.CreatePricingRuleCommandHandler>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Pricing.ActivatePricingPlanCommand, PricingPlanResponseDto>, Sayra.Backend.Application.Pricing.ActivatePricingPlanCommandHandler>();
            services.AddScoped<ICommandHandler<Sayra.Backend.Application.Pricing.DeactivatePricingPlanCommand, PricingPlanResponseDto>, Sayra.Backend.Application.Pricing.DeactivatePricingPlanCommandHandler>();
            services.AddScoped<IQueryHandler<Sayra.Backend.Application.Pricing.GetPricingPlanQuery, PricingPlanResponseDto>, Sayra.Backend.Application.Pricing.GetPricingPlanQueryHandler>();
            services.AddScoped<IQueryHandler<Sayra.Backend.Application.Pricing.GetPricingRulesQuery, System.Collections.Generic.List<PricingRuleResponseDto>>, Sayra.Backend.Application.Pricing.GetPricingRulesQueryHandler>();
            services.AddScoped<IQueryHandler<Sayra.Backend.Application.Pricing.ResolveRateQuery, ResolvedRateResponseDto>, Sayra.Backend.Application.Pricing.ResolveRateQueryHandler>();

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

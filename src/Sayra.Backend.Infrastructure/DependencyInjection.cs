using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
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

            // 5. TCP & UDP Transport Services
            services.AddSingleton<ITcpConnectionRegistry, TcpConnectionRegistry>();

            services.AddSingleton<TcpServer>();
            services.AddSingleton<ITcpServer>(provider => provider.GetRequiredService<TcpServer>());
            services.AddHostedService(provider => provider.GetRequiredService<TcpServer>());

            services.AddSingleton<UdpDiscoveryServer>();
            services.AddSingleton<IUdpDiscoveryServer>(provider => provider.GetRequiredService<UdpDiscoveryServer>());
            services.AddHostedService(provider => provider.GetRequiredService<UdpDiscoveryServer>());

            // 6. Health Checks
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

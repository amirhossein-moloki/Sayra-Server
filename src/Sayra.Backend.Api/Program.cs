using System;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Sayra.Backend.Api.Middleware;
using Sayra.Backend.Infrastructure;
using Sayra.Backend.Infrastructure.Configuration;
using Sayra.Backend.Infrastructure.Configuration.Options;
using Sayra.Backend.Infrastructure.Logging;

namespace Sayra.Backend.Api
{
    public class Program
    {
        public static int Main(string[] args)
        {
            EnvLoader.Load();

            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateBootstrapLogger();

            try
            {
                Log.Information("Starting SAYRA Central Backend...");

                var builder = WebApplication.CreateBuilder(args);

                // Configure Serilog
                builder.Host.UseSerilog((context, services, configuration) =>
                {
                    SerilogConfiguration.ConfigureLogging(context, configuration);
                });

                // Configure Kestrel limits
                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.Limits.MaxRequestBodySize = 52428800; // 50MB
                    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
                    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
                    options.Limits.MaxConcurrentConnections = 1000;
                    options.Limits.MaxConcurrentUpgradedConnections = 1000;
                });

                // Validate critical configuration on startup (Fail-Fast)
                var dbOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
                var redisOptions = builder.Configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();
                var serverOptions = builder.Configuration.GetSection(ServerOptions.SectionName).Get<ServerOptions>() ?? new ServerOptions();
                var discoveryOptions = builder.Configuration.GetSection(DiscoveryOptions.SectionName).Get<DiscoveryOptions>() ?? new DiscoveryOptions();
                var securityOptions = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();

                ConfigurationValidator.Validate(dbOptions, redisOptions, serverOptions, discoveryOptions, securityOptions);

                // Add Infrastructure dependencies
                builder.Services.AddInfrastructure(builder.Configuration);

                // Configure Controllers with camelCase serialization
                builder.Services.AddControllers()
                    .AddApplicationPart(typeof(Sayra.Backend.Api.Controllers.GamersController).Assembly)
                    .AddJsonOptions(options =>
                    {
                        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
                    });

                // Add Swagger/OpenAPI support
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen();

                var app = builder.Build();

                // Secure Headers Middleware
                app.Use(async (context, next) =>
                {
                    context.Response.Headers["X-Frame-Options"] = "DENY";
                    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
                    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'";
                    await next();
                });

                // Global Exception Handling Middleware (first in pipeline)
                app.UseMiddleware<ExceptionHandlingMiddleware>();

                // Configure HTTP request pipeline
                if (app.Environment.IsDevelopment())
                {
                    app.UseSwagger();
                    app.UseSwaggerUI();
                }

                app.UseRouting();

                // Authentication and Authorization (empty placeholders for stage 03+)
                app.UseAuthorization();

                // Map Controllers (including Health endpoints)
                app.MapControllers();

                app.Run();

                return 0;
            }
            catch (Exception ex) when (ex.GetType().Name != "HostAbortedException")
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}

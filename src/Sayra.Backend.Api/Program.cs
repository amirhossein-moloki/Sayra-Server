using System;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Sayra.Backend.Api.Middleware;
using Sayra.Backend.Infrastructure;
using Sayra.Backend.Infrastructure.Logging;

namespace Sayra.Backend.Api
{
    public class Program
    {
        public static int Main(string[] args)
        {
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

                // Add Infrastructure dependencies
                builder.Services.AddInfrastructure(builder.Configuration);

                // Configure Controllers with camelCase serialization
                builder.Services.AddControllers()
                    .AddJsonOptions(options =>
                    {
                        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
                    });

                // Add Swagger/OpenAPI support
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen();

                var app = builder.Build();

                // Global Exception Handling Middleware (first in pipeline)
                app.UseMiddleware<ExceptionHandlingMiddleware>();

                // Configure HTTP request pipeline
                if (app.Environment.IsDevelopment())
                {
                    app.UseSwagger();
                    app.UseSwaggerUI();
                }

                app.UseRouting();

                // Authentication and Authorization (empty place holders for stage 03+)
                app.UseAuthorization();

                // Map Controllers (including Health endpoints)
                app.MapControllers();

                app.Run();

                return 0;
            }
            catch (Exception ex)
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

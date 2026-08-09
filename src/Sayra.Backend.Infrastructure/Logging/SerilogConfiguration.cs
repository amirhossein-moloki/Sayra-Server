using System;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Infrastructure.Logging
{
    public static class SerilogConfiguration
    {
        public static void ConfigureLogging(HostBuilderContext context, LoggerConfiguration loggerConfiguration)
        {
            var env = context.HostingEnvironment;

            loggerConfiguration
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Service", "Sayra.Backend")
                .Enrich.WithProperty("Environment", env.EnvironmentName)
                .Enrich.With(new CorrelationIdEnricher())
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [CorrelationId: {CorrelationId}] [PC: {pcId}] [Connection: {connectionId}] [Session: {sessionId}] [Command: {commandId}] {Message:lj}{NewLine}{Exception}"
                )
                .WriteTo.File(
                    path: "logs/sayra-backend-.txt",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{CorrelationId}] {Message:lj} [PC: {pcId}] [Connection: {connectionId}] [Session: {sessionId}] [Command: {commandId}] [TraceId: {traceId}]{NewLine}{Exception}"
                );

            // Never log sensitive info
            // (handled by ensuring code does not log them, and optional destructuring rules if needed)
        }
    }

    public class CorrelationIdEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var correlationId = CorrelationContext.CorrelationId;
            if (string.IsNullOrEmpty(correlationId))
            {
                correlationId = Guid.NewGuid().ToString();
                CorrelationContext.CorrelationId = correlationId;
            }

            var correlationProp = propertyFactory.CreateProperty("CorrelationId", correlationId);
            logEvent.AddOrUpdateProperty(correlationProp);
        }
    }
}

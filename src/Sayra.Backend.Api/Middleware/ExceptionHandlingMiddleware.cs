using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Api.Models;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Api.Middleware
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                // Ensure correlation ID is set for this context
                var correlationId = context.Request.Headers["X-Correlation-ID"].ToString();
                if (string.IsNullOrEmpty(correlationId))
                {
                    correlationId = Guid.NewGuid().ToString();
                }
                CorrelationContext.SetCorrelationId(correlationId);
                context.Response.Headers["X-Correlation-ID"] = correlationId;

                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            var traceId = context.TraceIdentifier;
            var correlationId = CorrelationContext.CorrelationId;

            int statusCode;
            string code;
            string title;
            string type;

            if (exception is DomainException domainException)
            {
                code = domainException.ErrorCode;
                title = domainException.Message;
                type = $"https://sayra.local/errors/{code.ToLowerInvariant()}";

                statusCode = domainException switch
                {
                    AuthFailedException => (int)HttpStatusCode.Unauthorized,
                    SessionExpiredException => (int)HttpStatusCode.Unauthorized,
                    DeviceNotRegisteredException => (int)HttpStatusCode.Forbidden,
                    InvalidCommandException => (int)HttpStatusCode.BadRequest,
                    _ => (int)HttpStatusCode.BadRequest
                };

                _logger.LogWarning(exception, "Domain exception occurred. Code: {Code}, Message: {Message}, CorrelationId: {CorrelationId}", code, title, correlationId);
            }
            else
            {
                statusCode = (int)HttpStatusCode.InternalServerError;
                code = "INTERNAL_ERROR";
                title = "An unexpected error occurred on the server.";
                type = "https://sayra.local/errors/internal-error";

                _logger.LogError(exception, "Unhandled generic exception occurred. CorrelationId: {CorrelationId}", correlationId);
            }

            var response = new ErrorResponse
            {
                Type = type,
                Title = title,
                Status = statusCode,
                Code = code,
                TraceId = traceId
            };

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = statusCode;

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            var jsonResponse = JsonSerializer.Serialize(response, jsonOptions);
            await context.Response.WriteAsync(jsonResponse);
        }
    }
}

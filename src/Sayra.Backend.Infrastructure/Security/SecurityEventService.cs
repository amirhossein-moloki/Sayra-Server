using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Infrastructure.Security
{
    public class SecurityEventService : ISecurityEventService
    {
        private readonly IRepository<SecurityEvent> _securityEventRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<SecurityEventService> _logger;

        private static readonly Regex SensitivePatternRegex = new Regex(
            @"(password|passwordhash|accesstoken|refreshtoken|sessionsecret|privatekey|encryptionkey|token|secret)\s*[:=]\s*['""]?([^'"";,\s]+)['""]?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public SecurityEventService(
            IRepository<SecurityEvent> securityEventRepository,
            IUnitOfWork unitOfWork,
            ILogger<SecurityEventService> logger)
        {
            _securityEventRepository = securityEventRepository ?? throw new ArgumentNullException(nameof(securityEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task RecordSecurityEventAsync(SecurityEvent securityEvent, CancellationToken cancellationToken = default)
        {
            if (securityEvent == null) return;

            securityEvent.FailureReason = SanitizeSensitiveData(securityEvent.FailureReason);
            securityEvent.Action = SanitizeSensitiveData(securityEvent.Action);
            securityEvent.CorrelationId ??= CorrelationContext.CorrelationId;

            securityEvent.NormalizeAndValidate();

            try
            {
                await _securityEventRepository.AddAsync(securityEvent, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "SecurityEvent recorded: {EventType}, Result: {Result}, Actor: {ActorId}, Device: {DeviceId}, CorrelationId: {CorrelationId}",
                    securityEvent.EventType, securityEvent.Result, securityEvent.ActorId, securityEvent.DeviceId, securityEvent.CorrelationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist SecurityEvent {EventType}", securityEvent.EventType);
            }
        }

        public async Task RecordSecurityEventAsync(
            string eventType,
            Guid? actorId,
            string? actorType,
            string? deviceId,
            Guid? organizationId,
            Guid? siteId,
            string? resourceType,
            Guid? resourceId,
            string? action,
            string result,
            string? failureReason,
            string? correlationId = null,
            string? traceId = null,
            CancellationToken cancellationToken = default)
        {
            var securityEvent = new SecurityEvent
            {
                EventType = eventType,
                ActorId = actorId,
                ActorType = actorType,
                DeviceId = deviceId,
                OrganizationId = organizationId,
                SiteId = siteId,
                ResourceType = resourceType,
                ResourceId = resourceId,
                Action = action,
                Result = result,
                FailureReason = failureReason,
                CorrelationId = correlationId ?? CorrelationContext.CorrelationId,
                TraceId = traceId,
                CreatedAt = DateTime.UtcNow
            };

            await RecordSecurityEventAsync(securityEvent, cancellationToken);
        }

        private static string? SanitizeSensitiveData(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            return SensitivePatternRegex.Replace(input, "$1=[REDACTED]");
        }
    }
}

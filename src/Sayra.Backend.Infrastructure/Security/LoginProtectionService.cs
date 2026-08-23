using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Security
{
    public class LoginProtectionService : ILoginProtectionService
    {
        private readonly IRedisService _redisService;
        private readonly IRepository<LoginAttempt> _loginAttemptRepository;
        private readonly IRepository<User> _userRepository;
        private readonly IRepository<Gamer> _gamerRepository;
        private readonly IRepository<GamerCredential> _gamerCredentialRepository;
        private readonly ISecurityEventService _securityEventService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<LoginProtectionService> _logger;

        private const int MaxFailedAttempts = 5;
        private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);

        public LoginProtectionService(
            IRedisService redisService,
            IRepository<LoginAttempt> loginAttemptRepository,
            IRepository<User> userRepository,
            IRepository<Gamer> gamerRepository,
            IRepository<GamerCredential> gamerCredentialRepository,
            ISecurityEventService securityEventService,
            IUnitOfWork unitOfWork,
            ILogger<LoginProtectionService> logger)
        {
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
            _loginAttemptRepository = loginAttemptRepository ?? throw new ArgumentNullException(nameof(loginAttemptRepository));
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _gamerRepository = gamerRepository ?? throw new ArgumentNullException(nameof(gamerRepository));
            _gamerCredentialRepository = gamerCredentialRepository ?? throw new ArgumentNullException(nameof(gamerCredentialRepository));
            _securityEventService = securityEventService ?? throw new ArgumentNullException(nameof(securityEventService));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> IsLockedOutAsync(string usernameOrIp, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(usernameOrIp)) return false;

            string normalized = usernameOrIp.Trim().ToLowerInvariant();
            string lockKey = $"sayra:login:lockout:{normalized}";

            // Fast path: Redis check
            try
            {
                var lockVal = await _redisService.GetStringAsync(lockKey, cancellationToken);
                if (string.Equals(lockVal, "1", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis check failed for key {Key}, falling back to DB context", lockKey);
            }

            // Source of truth: PostgreSQL check
            var serverNow = DateTime.UtcNow;

            var dbRecord = await _loginAttemptRepository.FirstOrDefaultAsync(
                x => x.UsernameIdentifier == normalized,
                track: false,
                cancellationToken: cancellationToken);

            if (dbRecord != null && dbRecord.LockedUntil.HasValue && dbRecord.LockedUntil.Value > serverNow)
            {
                // Sync lock back to Redis
                try
                {
                    await _redisService.SetStringAsync(lockKey, "1", dbRecord.LockedUntil.Value - serverNow, cancellationToken);
                }
                catch
                {
                    // Ignore Redis caching failures
                }
                return true;
            }

            // Check User entity lockout
            var user = await _userRepository.FirstOrDefaultAsync(u => u.Username == normalized || u.Email == normalized, track: false, cancellationToken);
            if (user != null && user.IsCurrentlyLockedOut())
            {
                return true;
            }

            // Check GamerCredential entity lockout
            var gamer = await _gamerRepository.FirstOrDefaultAsync(g => g.Username == normalized || g.Email == normalized, track: false, cancellationToken);
            if (gamer != null)
            {
                var gamerCred = await _gamerCredentialRepository.FirstOrDefaultAsync(gc => gc.GamerEntityId == gamer.Id, track: false, cancellationToken);
                if (gamerCred != null && gamerCred.IsCurrentlyLockedOut())
                {
                    return true;
                }
            }

            return false;
        }

        public async Task RecordFailedAttemptAsync(
            string usernameOrIp,
            Guid? userId = null,
            string? ipAddress = null,
            string? deviceId = null,
            string? failureReason = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(usernameOrIp)) return;

            string normalized = usernameOrIp.Trim().ToLowerInvariant();
            string attemptKey = $"sayra:login:attempts:{normalized}";
            string lockKey = $"sayra:login:lockout:{normalized}";

            var serverNow = DateTime.UtcNow;

            // Update Redis fast counters (Atomic increment)
            int currentRedisAttempts = 1;
            try
            {
                long val = await _redisService.IncrementAsync(attemptKey, LockoutWindow, cancellationToken);
                currentRedisAttempts = (int)val;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update Redis attempts for key {Key}", attemptKey);
            }

            // Update PostgreSQL source of truth
            var record = await _loginAttemptRepository.FirstOrDefaultAsync(
                x => x.UsernameIdentifier == normalized,
                track: true,
                cancellationToken: cancellationToken);

            if (record == null)
            {
                record = new LoginAttempt
                {
                    UsernameIdentifier = normalized,
                    UserId = userId,
                    IpAddress = ipAddress,
                    DeviceId = deviceId,
                    Success = false,
                    FailureReason = failureReason,
                    AttemptCount = 1,
                    LastAttemptAt = serverNow,
                    CreatedAt = serverNow
                };
                if (currentRedisAttempts >= MaxFailedAttempts)
                {
                    record.LockedUntil = serverNow.Add(LockoutWindow);
                }
                record.NormalizeAndValidate();
                await _loginAttemptRepository.AddAsync(record, cancellationToken);
            }
            else
            {
                record.AttemptCount++;
                record.LastAttemptAt = serverNow;
                record.IpAddress = ipAddress ?? record.IpAddress;
                record.DeviceId = deviceId ?? record.DeviceId;
                record.FailureReason = failureReason;
                record.Success = false;

                if (record.AttemptCount >= MaxFailedAttempts || currentRedisAttempts >= MaxFailedAttempts)
                {
                    record.LockedUntil = serverNow.Add(LockoutWindow);
                }
                record.NormalizeAndValidate();
                _loginAttemptRepository.Update(record);
            }

            // Lock User / GamerCredential entity if applicable
            var user = await _userRepository.FirstOrDefaultAsync(u => u.Username == normalized || u.Email == normalized, track: true, cancellationToken);
            if (user != null)
            {
                user.RecordFailedLoginAttempt(MaxFailedAttempts, LockoutWindow);
                _userRepository.Update(user);
            }

            var gamer = await _gamerRepository.FirstOrDefaultAsync(g => g.Username == normalized || g.Email == normalized, track: false, cancellationToken);
            if (gamer != null)
            {
                var gamerCred = await _gamerCredentialRepository.FirstOrDefaultAsync(gc => gc.GamerEntityId == gamer.Id, track: true, cancellationToken);
                if (gamerCred != null)
                {
                    gamerCred.RecordFailedAttempt(MaxFailedAttempts, LockoutWindow);
                    _gamerCredentialRepository.Update(gamerCred);
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            bool isLockedNow = record.LockedUntil.HasValue && record.LockedUntil.Value > serverNow;

            if (isLockedNow)
            {
                try
                {
                    await _redisService.SetStringAsync(lockKey, "1", LockoutWindow, cancellationToken);
                }
                catch
                {
                    // Ignore Redis failure
                }

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "ACCOUNT_LOCKED",
                    actorId: userId ?? user?.Id ?? gamer?.Id,
                    actorType: user != null ? "User" : (gamer != null ? "Gamer" : "ANONYMOUS"),
                    deviceId: deviceId,
                    organizationId: user?.OrganizationEntityId,
                    siteId: user?.SiteEntityId,
                    resourceType: "Account",
                    resourceId: userId ?? user?.Id ?? gamer?.Id,
                    action: "LOGIN",
                    result: "LOCKED",
                    failureReason: $"Account locked due to {MaxFailedAttempts} consecutive failed attempts.",
                    cancellationToken: cancellationToken);
            }
            else
            {
                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "LOGIN_FAILED",
                    actorId: userId ?? user?.Id ?? gamer?.Id,
                    actorType: user != null ? "User" : (gamer != null ? "Gamer" : "ANONYMOUS"),
                    deviceId: deviceId,
                    organizationId: user?.OrganizationEntityId,
                    siteId: user?.SiteEntityId,
                    resourceType: "Account",
                    resourceId: userId ?? user?.Id ?? gamer?.Id,
                    action: "LOGIN",
                    result: "FAILED",
                    failureReason: failureReason ?? "Invalid credentials",
                    cancellationToken: cancellationToken);
            }
        }

        public async Task ResetAttemptsAsync(string usernameOrIp, Guid? userId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(usernameOrIp)) return;

            string normalized = usernameOrIp.Trim().ToLowerInvariant();
            string attemptKey = $"sayra:login:attempts:{normalized}";
            string lockKey = $"sayra:login:lockout:{normalized}";

            try
            {
                await _redisService.RemoveAsync(attemptKey, cancellationToken);
                await _redisService.RemoveAsync(lockKey, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove Redis keys for {Normalized}", normalized);
            }

            var record = await _loginAttemptRepository.FirstOrDefaultAsync(
                x => x.UsernameIdentifier == normalized,
                track: true,
                cancellationToken: cancellationToken);

            if (record != null)
            {
                record.AttemptCount = 0;
                record.LockedUntil = null;
                record.Success = true;
                record.LastAttemptAt = DateTime.UtcNow;
                _loginAttemptRepository.Update(record);
            }

            var user = await _userRepository.FirstOrDefaultAsync(u => u.Username == normalized || u.Email == normalized, track: true, cancellationToken);
            if (user != null)
            {
                user.ResetFailedLoginAttempts();
                _userRepository.Update(user);
            }

            var gamer = await _gamerRepository.FirstOrDefaultAsync(g => g.Username == normalized || g.Email == normalized, track: false, cancellationToken);
            if (gamer != null)
            {
                var gamerCred = await _gamerCredentialRepository.FirstOrDefaultAsync(gc => gc.GamerEntityId == gamer.Id, track: true, cancellationToken);
                if (gamerCred != null)
                {
                    gamerCred.ResetFailedAttempts();
                    _gamerCredentialRepository.Update(gamerCred);
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task UnlockAsync(string usernameOrIp, CancellationToken cancellationToken = default)
        {
            await ResetAttemptsAsync(usernameOrIp, null, cancellationToken);

            string normalized = usernameOrIp.Trim().ToLowerInvariant();
            var user = await _userRepository.FirstOrDefaultAsync(u => u.Username == normalized || u.Email == normalized, track: false, cancellationToken);
            var gamer = await _gamerRepository.FirstOrDefaultAsync(g => g.Username == normalized || g.Email == normalized, track: false, cancellationToken);

            await _securityEventService.RecordSecurityEventAsync(
                eventType: "ACCOUNT_UNLOCKED",
                actorId: user?.Id ?? gamer?.Id,
                actorType: user != null ? "User" : (gamer != null ? "Gamer" : "ANONYMOUS"),
                deviceId: null,
                organizationId: user?.OrganizationEntityId,
                siteId: user?.SiteEntityId,
                resourceType: "Account",
                resourceId: user?.Id ?? gamer?.Id,
                action: "UNLOCK",
                result: "SUCCESS",
                failureReason: null,
                cancellationToken: cancellationToken);
        }
    }
}

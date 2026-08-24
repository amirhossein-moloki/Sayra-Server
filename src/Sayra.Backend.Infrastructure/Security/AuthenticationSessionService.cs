using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Infrastructure.Security
{
    public class AuthenticationSessionService : IAuthenticationSessionService
    {
        private readonly IRepository<AuthenticationSession> _sessionRepository;
        private readonly IRedisService _redisService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<AuthenticationSessionService> _logger;

        private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(24);

        public AuthenticationSessionService(
            IRepository<AuthenticationSession> sessionRepository,
            IRedisService redisService,
            IUnitOfWork unitOfWork,
            ILogger<AuthenticationSessionService> logger)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AuthenticationSession> CreateSessionAsync(
            Guid? userId,
            Guid? gamerId,
            string? pcId = null,
            Guid? deviceId = null,
            TimeSpan? lifetime = null,
            string? createdBy = null,
            string? ipAddress = null,
            string? userAgent = null,
            CancellationToken cancellationToken = default)
        {
            string token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            var effectiveLifetime = lifetime ?? DefaultLifetime;
            var serverNow = DateTime.UtcNow;

            var session = new AuthenticationSession
            {
                SessionToken = token,
                UserId = userId,
                GamerId = gamerId,
                PcId = pcId,
                DeviceId = deviceId,
                Status = AuthenticationSession.StatusActive,
                CreatedAt = serverNow,
                ExpiresAt = serverNow.Add(effectiveLifetime),
                CreatedBy = createdBy ?? "SYSTEM",
                IpAddress = ipAddress,
                UserAgent = userAgent
            };

            await _sessionRepository.AddAsync(session, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            try
            {
                string cacheKey = $"sayra:auth:session:{token}";
                await _redisService.SetAsync(cacheKey, session, effectiveLifetime, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cache authentication session {Token} in Redis", token);
            }

            return session;
        }

        public async Task<AuthenticationSession?> GetSessionByTokenAsync(string token, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            string trimmedToken = token.Trim();
            string cacheKey = $"sayra:auth:session:{trimmedToken}";

            try
            {
                var cached = await _redisService.GetAsync<AuthenticationSession>(cacheKey, cancellationToken);
                if (cached != null) return cached;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve authentication session from Redis cache for key {Key}", cacheKey);
            }

            return await _sessionRepository.FirstOrDefaultAsync(s => s.SessionToken == trimmedToken, track: false, cancellationToken: cancellationToken);
        }

        public async Task<bool> ValidateSessionAsync(string token, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;

            string trimmedToken = token.Trim();
            string revokedKey = $"sayra:auth:revoked:{trimmedToken}";

            try
            {
                var revokedVal = await _redisService.GetStringAsync(revokedKey, cancellationToken);
                if (!string.IsNullOrEmpty(revokedVal)) return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check revocation status in Redis for key {Key}", revokedKey);
            }

            var session = await GetSessionByTokenAsync(trimmedToken, cancellationToken);
            if (session == null) return false;

            if (session.Status != AuthenticationSession.StatusActive) return false;
            if (session.ExpiresAt <= DateTime.UtcNow) return false;
            if (session.RevokedAt.HasValue) return false;

            return true;
        }

        public async Task<bool> RevokeSessionAsync(string token, string reason, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;

            string trimmedToken = token.Trim();
            var session = await _sessionRepository.FirstOrDefaultAsync(s => s.SessionToken == trimmedToken, track: true, cancellationToken: cancellationToken);
            if (session == null) return false;

            session.Status = AuthenticationSession.StatusRevoked;
            session.RevokedAt = DateTime.UtcNow;
            session.RevocationReason = reason;

            _sessionRepository.Update(session);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            string cacheKey = $"sayra:auth:session:{trimmedToken}";
            string revokedKey = $"sayra:auth:revoked:{trimmedToken}";

            try
            {
                await _redisService.RemoveAsync(cacheKey, cancellationToken);
                await _redisService.SetStringAsync(revokedKey, "1", TimeSpan.FromHours(24), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update Redis revocation keys for token {Token}", trimmedToken);
            }

            return true;
        }

        public async Task<int> RevokeAllUserSessionsAsync(Guid userId, string reason, CancellationToken cancellationToken = default)
        {
            var sessions = await _sessionRepository.FindAsync(s => s.UserId == userId && s.Status == AuthenticationSession.StatusActive, track: true, cancellationToken: cancellationToken);
            int count = 0;
            foreach (var session in sessions)
            {
                session.Status = AuthenticationSession.StatusRevoked;
                session.RevokedAt = DateTime.UtcNow;
                session.RevocationReason = reason;
                _sessionRepository.Update(session);

                string cacheKey = $"sayra:auth:session:{session.SessionToken}";
                string revokedKey = $"sayra:auth:revoked:{session.SessionToken}";
                try
                {
                    await _redisService.RemoveAsync(cacheKey, cancellationToken);
                    await _redisService.SetStringAsync(revokedKey, "1", TimeSpan.FromHours(24), cancellationToken);
                }
                catch { }

                count++;
            }

            if (count > 0)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return count;
        }

        public async Task<int> RevokeAllGamerSessionsAsync(Guid gamerId, string reason, CancellationToken cancellationToken = default)
        {
            var sessions = await _sessionRepository.FindAsync(s => s.GamerId == gamerId && s.Status == AuthenticationSession.StatusActive, track: true, cancellationToken: cancellationToken);
            int count = 0;
            foreach (var session in sessions)
            {
                session.Status = AuthenticationSession.StatusRevoked;
                session.RevokedAt = DateTime.UtcNow;
                session.RevocationReason = reason;
                _sessionRepository.Update(session);

                string cacheKey = $"sayra:auth:session:{session.SessionToken}";
                string revokedKey = $"sayra:auth:revoked:{session.SessionToken}";
                try
                {
                    await _redisService.RemoveAsync(cacheKey, cancellationToken);
                    await _redisService.SetStringAsync(revokedKey, "1", TimeSpan.FromHours(24), cancellationToken);
                }
                catch { }

                count++;
            }

            if (count > 0)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return count;
        }

        public async Task<int> RevokeAllDeviceSessionsAsync(string pcId, string reason, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pcId)) return 0;

            var sessions = await _sessionRepository.FindAsync(s => s.PcId == pcId && s.Status == AuthenticationSession.StatusActive, track: true, cancellationToken: cancellationToken);
            int count = 0;
            foreach (var session in sessions)
            {
                session.Status = AuthenticationSession.StatusRevoked;
                session.RevokedAt = DateTime.UtcNow;
                session.RevocationReason = reason;
                _sessionRepository.Update(session);

                string cacheKey = $"sayra:auth:session:{session.SessionToken}";
                string revokedKey = $"sayra:auth:revoked:{session.SessionToken}";
                try
                {
                    await _redisService.RemoveAsync(cacheKey, cancellationToken);
                    await _redisService.SetStringAsync(revokedKey, "1", TimeSpan.FromHours(24), cancellationToken);
                }
                catch { }

                count++;
            }

            if (count > 0)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return count;
        }
    }
}

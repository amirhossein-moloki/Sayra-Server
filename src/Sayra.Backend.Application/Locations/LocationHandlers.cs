using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Events;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Locations
{
    public class CreateSiteCommandHandler : ICommandHandler<CreateSiteCommand, Site>
    {
        private readonly IRepository<Site> _siteRepository;
        private readonly IRepository<Organization> _organizationRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CreateSiteCommandHandler(
            IRepository<Site> siteRepository,
            IRepository<Organization> organizationRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _siteRepository = siteRepository ?? throw new ArgumentNullException(nameof(siteRepository));
            _organizationRepository = organizationRepository ?? throw new ArgumentNullException(nameof(organizationRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<Site>> HandleAsync(CreateSiteCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new CreateSiteCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<Site>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var org = await _organizationRepository.GetByIdAsync(command.OrganizationId, track: false, cancellationToken);
                if (org == null)
                {
                    return Result<Site>.Failure("ORGANIZATION_NOT_FOUND", $"Organization with ID '{command.OrganizationId}' not found.");
                }

                if (!org.CanOperate())
                {
                    return Result<Site>.Failure("ORGANIZATION_INACTIVE", $"Organization '{org.Name}' is not active and cannot create new sites.");
                }

                var codeUpper = command.Code.Trim().ToUpperInvariant();
                var allSites = await _siteRepository.GetAllAsync(track: false, cancellationToken);
                if (allSites.Any(s => s.OrganizationId == command.OrganizationId && s.Code.Equals(codeUpper, StringComparison.OrdinalIgnoreCase)))
                {
                    return Result<Site>.Failure("DUPLICATE_SITE_CODE", $"Site with code '{codeUpper}' already exists under Organization '{org.Name}'.");
                }

                var site = new Site
                {
                    OrganizationId = command.OrganizationId,
                    Name = command.Name,
                    Code = command.Code,
                    Timezone = command.Timezone,
                    Status = "Active"
                };

                site.NormalizeAndValidate();

                await _siteRepository.AddAsync(site, cancellationToken);

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(SiteCreated),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = System.Text.Json.JsonSerializer.Serialize(new SiteCreated(
                        site.Id,
                        site.OrganizationId,
                        site.Code,
                        site.Name,
                        site.Status,
                        site.Timezone,
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<Site>.Success(site);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<Site>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<Site>.Failure("CREATE_SITE_FAILED", ex.Message);
            }
        }
    }

    public class DeactivateSiteCommandHandler : ICommandHandler<DeactivateSiteCommand, Site>
    {
        private readonly IRepository<Site> _siteRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public DeactivateSiteCommandHandler(
            IRepository<Site> siteRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _siteRepository = siteRepository ?? throw new ArgumentNullException(nameof(siteRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<Site>> HandleAsync(DeactivateSiteCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new DeactivateSiteCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<Site>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var site = await _siteRepository.GetByIdAsync(command.SiteId, track: true, cancellationToken);
                if (site == null)
                {
                    return Result<Site>.Failure("NOT_FOUND", $"Site with ID '{command.SiteId}' not found.");
                }

                site.Deactivate();
                _siteRepository.Update(site);

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(SiteDeactivated),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = System.Text.Json.JsonSerializer.Serialize(new SiteDeactivated(
                        site.Id,
                        site.OrganizationId,
                        site.Code,
                        site.Status,
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<Site>.Success(site);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<Site>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<Site>.Failure("DEACTIVATE_SITE_FAILED", ex.Message);
            }
        }
    }

    public class GetSiteQueryHandler : IQueryHandler<GetSiteQuery, Site>
    {
        private readonly IRepository<Site> _siteRepository;

        public GetSiteQueryHandler(IRepository<Site> siteRepository)
        {
            _siteRepository = siteRepository ?? throw new ArgumentNullException(nameof(siteRepository));
        }

        public async Task<Result<Site>> HandleAsync(GetSiteQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new GetSiteQueryValidator();
            var validationResult = await validator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<Site>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            var site = await _siteRepository.GetByIdAsync(query.SiteId, track: false, cancellationToken);
            if (site == null)
            {
                return Result<Site>.Failure("NOT_FOUND", $"Site with ID '{query.SiteId}' not found.");
            }

            return Result<Site>.Success(site);
        }
    }

    public class CreateZoneCommandHandler : ICommandHandler<CreateZoneCommand, Zone>
    {
        private readonly IRepository<Zone> _zoneRepository;
        private readonly IRepository<Site> _siteRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CreateZoneCommandHandler(
            IRepository<Zone> zoneRepository,
            IRepository<Site> siteRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _zoneRepository = zoneRepository ?? throw new ArgumentNullException(nameof(zoneRepository));
            _siteRepository = siteRepository ?? throw new ArgumentNullException(nameof(siteRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<Zone>> HandleAsync(CreateZoneCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new CreateZoneCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<Zone>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var site = await _siteRepository.GetByIdAsync(command.SiteId, track: false, cancellationToken);
                if (site == null)
                {
                    return Result<Zone>.Failure("SITE_NOT_FOUND", $"Site with ID '{command.SiteId}' not found.");
                }

                if (!site.CanOperate())
                {
                    return Result<Zone>.Failure("SITE_INACTIVE", $"Site '{site.Name}' is not active and cannot accept new zones.");
                }

                var codeUpper = command.Code.Trim().ToUpperInvariant();
                var allZones = await _zoneRepository.GetAllAsync(track: false, cancellationToken);
                if (allZones.Any(z => z.SiteId == command.SiteId && z.Code.Equals(codeUpper, StringComparison.OrdinalIgnoreCase)))
                {
                    return Result<Zone>.Failure("DUPLICATE_ZONE_CODE", $"Zone with code '{codeUpper}' already exists under Site '{site.Name}'.");
                }

                var zone = new Zone
                {
                    SiteId = command.SiteId,
                    Name = command.Name,
                    Code = command.Code,
                    Status = "Active"
                };

                zone.NormalizeAndValidate();

                await _zoneRepository.AddAsync(zone, cancellationToken);

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(ZoneCreated),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = System.Text.Json.JsonSerializer.Serialize(new ZoneCreated(
                        zone.Id,
                        zone.SiteId,
                        zone.Code,
                        zone.Name,
                        zone.Status,
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<Zone>.Success(zone);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<Zone>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<Zone>.Failure("CREATE_ZONE_FAILED", ex.Message);
            }
        }
    }

    public class DeactivateZoneCommandHandler : ICommandHandler<DeactivateZoneCommand, Zone>
    {
        private readonly IRepository<Zone> _zoneRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public DeactivateZoneCommandHandler(
            IRepository<Zone> zoneRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _zoneRepository = zoneRepository ?? throw new ArgumentNullException(nameof(zoneRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<Zone>> HandleAsync(DeactivateZoneCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new DeactivateZoneCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<Zone>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var zone = await _zoneRepository.GetByIdAsync(command.ZoneId, track: true, cancellationToken);
                if (zone == null)
                {
                    return Result<Zone>.Failure("NOT_FOUND", $"Zone with ID '{command.ZoneId}' not found.");
                }

                zone.Deactivate();
                _zoneRepository.Update(zone);

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(ZoneDeactivated),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = System.Text.Json.JsonSerializer.Serialize(new ZoneDeactivated(
                        zone.Id,
                        zone.SiteId,
                        zone.Code,
                        zone.Status,
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<Zone>.Success(zone);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<Zone>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<Zone>.Failure("DEACTIVATE_ZONE_FAILED", ex.Message);
            }
        }
    }

    public class GetZoneQueryHandler : IQueryHandler<GetZoneQuery, Zone>
    {
        private readonly IRepository<Zone> _zoneRepository;

        public GetZoneQueryHandler(IRepository<Zone> zoneRepository)
        {
            _zoneRepository = zoneRepository ?? throw new ArgumentNullException(nameof(zoneRepository));
        }

        public async Task<Result<Zone>> HandleAsync(GetZoneQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new GetZoneQueryValidator();
            var validationResult = await validator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<Zone>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            var zone = await _zoneRepository.GetByIdAsync(query.ZoneId, track: false, cancellationToken);
            if (zone == null)
            {
                return Result<Zone>.Failure("NOT_FOUND", $"Zone with ID '{query.ZoneId}' not found.");
            }

            return Result<Zone>.Success(zone);
        }
    }
}

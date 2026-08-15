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

namespace Sayra.Backend.Application.Organizations
{
    public class CreateOrganizationCommandHandler : ICommandHandler<CreateOrganizationCommand, Organization>
    {
        private readonly IRepository<Organization> _organizationRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CreateOrganizationCommandHandler(
            IRepository<Organization> organizationRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _organizationRepository = organizationRepository ?? throw new ArgumentNullException(nameof(organizationRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<Organization>> HandleAsync(CreateOrganizationCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new CreateOrganizationCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<Organization>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var codeUpper = command.Code.Trim().ToUpperInvariant();

                var allOrgs = await _organizationRepository.GetAllAsync(track: false, cancellationToken);
                if (allOrgs.Any(o => o.Code.Equals(codeUpper, StringComparison.OrdinalIgnoreCase)))
                {
                    return Result<Organization>.Failure("DUPLICATE_ORGANIZATION_CODE", $"Organization with code '{codeUpper}' already exists.");
                }

                var organization = new Organization
                {
                    Name = command.Name,
                    Code = command.Code,
                    Status = "Active"
                };

                organization.NormalizeAndValidate();

                await _organizationRepository.AddAsync(organization, cancellationToken);

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(OrganizationCreated),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = System.Text.Json.JsonSerializer.Serialize(new OrganizationCreated(
                        organization.Id,
                        organization.Code,
                        organization.Name,
                        organization.Status,
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<Organization>.Success(organization);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<Organization>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<Organization>.Failure("CREATE_ORGANIZATION_FAILED", ex.Message);
            }
        }
    }

    public class DeactivateOrganizationCommandHandler : ICommandHandler<DeactivateOrganizationCommand, Organization>
    {
        private readonly IRepository<Organization> _organizationRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public DeactivateOrganizationCommandHandler(
            IRepository<Organization> organizationRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _organizationRepository = organizationRepository ?? throw new ArgumentNullException(nameof(organizationRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<Organization>> HandleAsync(DeactivateOrganizationCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new DeactivateOrganizationCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<Organization>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var organization = await _organizationRepository.GetByIdAsync(command.OrganizationId, track: true, cancellationToken);
                if (organization == null)
                {
                    return Result<Organization>.Failure("NOT_FOUND", $"Organization with ID '{command.OrganizationId}' not found.");
                }

                organization.Deactivate();
                _organizationRepository.Update(organization);

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(OrganizationDeactivated),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = System.Text.Json.JsonSerializer.Serialize(new OrganizationDeactivated(
                        organization.Id,
                        organization.Code,
                        organization.Status,
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<Organization>.Success(organization);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<Organization>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<Organization>.Failure("DEACTIVATE_ORGANIZATION_FAILED", ex.Message);
            }
        }
    }

    public class GetOrganizationQueryHandler : IQueryHandler<GetOrganizationQuery, Organization>
    {
        private readonly IRepository<Organization> _organizationRepository;

        public GetOrganizationQueryHandler(IRepository<Organization> organizationRepository)
        {
            _organizationRepository = organizationRepository ?? throw new ArgumentNullException(nameof(organizationRepository));
        }

        public async Task<Result<Organization>> HandleAsync(GetOrganizationQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new GetOrganizationQueryValidator();
            var validationResult = await validator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<Organization>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            var organization = await _organizationRepository.GetByIdAsync(query.OrganizationId, track: false, cancellationToken);
            if (organization == null)
            {
                return Result<Organization>.Failure("NOT_FOUND", $"Organization with ID '{query.OrganizationId}' not found.");
            }

            return Result<Organization>.Success(organization);
        }
    }
}

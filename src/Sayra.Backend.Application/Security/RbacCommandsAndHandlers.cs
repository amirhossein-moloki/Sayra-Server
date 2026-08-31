using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Security
{
    public class CreateRoleCommand : ICommand<Role>
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public UserPrincipal ActingPrincipal { get; set; } = UserPrincipal.Anonymous;
    }

    public class AssignRoleToUserCommand : ICommand<bool>
    {
        public Guid UserEntityId { get; set; }
        public string RoleCode { get; set; } = string.Empty;
        public UserPrincipal ActingPrincipal { get; set; } = UserPrincipal.Anonymous;
    }

    public class RemoveRoleFromUserCommand : ICommand<bool>
    {
        public Guid UserEntityId { get; set; }
        public string RoleCode { get; set; } = string.Empty;
        public UserPrincipal ActingPrincipal { get; set; } = UserPrincipal.Anonymous;
    }

    public class AssignPermissionToRoleCommand : ICommand<bool>
    {
        public string RoleCode { get; set; } = string.Empty;
        public string PermissionCode { get; set; } = string.Empty;
        public UserPrincipal ActingPrincipal { get; set; } = UserPrincipal.Anonymous;
    }

    public class RemovePermissionFromRoleCommand : ICommand<bool>
    {
        public string RoleCode { get; set; } = string.Empty;
        public string PermissionCode { get; set; } = string.Empty;
        public UserPrincipal ActingPrincipal { get; set; } = UserPrincipal.Anonymous;
    }

    public class DisableRoleCommand : ICommand<bool>
    {
        public string RoleCode { get; set; } = string.Empty;
        public UserPrincipal ActingPrincipal { get; set; } = UserPrincipal.Anonymous;
    }

    public class DisablePermissionCommand : ICommand<bool>
    {
        public string PermissionCode { get; set; } = string.Empty;
        public UserPrincipal ActingPrincipal { get; set; } = UserPrincipal.Anonymous;
    }

    public class GetRolesQuery : IQuery<List<Role>>
    {
    }

    public class GetUserRolesQuery : IQuery<List<Role>>
    {
        public Guid UserEntityId { get; set; }
    }

    public class GetRolePermissionsQuery : IQuery<List<Permission>>
    {
        public string RoleCode { get; set; } = string.Empty;
    }

    public class GetPermissionsQuery : IQuery<List<Permission>>
    {
    }

    public class GetUserPermissionsQuery : IQuery<List<string>>
    {
        public Guid UserEntityId { get; set; }
    }

    public class CheckPermissionQuery : IQuery<bool>
    {
        public Guid UserEntityId { get; set; }
        public string PermissionCode { get; set; } = string.Empty;
    }

    public class RbacHandlers :
        ICommandHandler<CreateRoleCommand, Role>,
        ICommandHandler<AssignRoleToUserCommand, bool>,
        ICommandHandler<RemoveRoleFromUserCommand, bool>,
        ICommandHandler<AssignPermissionToRoleCommand, bool>,
        ICommandHandler<RemovePermissionFromRoleCommand, bool>,
        ICommandHandler<DisableRoleCommand, bool>,
        ICommandHandler<DisablePermissionCommand, bool>,
        IQueryHandler<GetRolesQuery, List<Role>>,
        IQueryHandler<GetUserRolesQuery, List<Role>>,
        IQueryHandler<GetRolePermissionsQuery, List<Permission>>,
        IQueryHandler<GetPermissionsQuery, List<Permission>>,
        IQueryHandler<GetUserPermissionsQuery, List<string>>,
        IQueryHandler<CheckPermissionQuery, bool>
    {
        private readonly IRepository<User> _userRepository;
        private readonly IRepository<Role> _roleRepository;
        private readonly IRepository<Permission> _permissionRepository;
        private readonly IRepository<UserRoleEntity> _userRoleRepository;
        private readonly IRepository<RolePermission> _rolePermissionRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IAuthorizationService _authorizationService;
        private readonly IUnitOfWork _unitOfWork;

        public RbacHandlers(
            IRepository<User> userRepository,
            IRepository<Role> roleRepository,
            IRepository<Permission> permissionRepository,
            IRepository<UserRoleEntity> userRoleRepository,
            IRepository<RolePermission> rolePermissionRepository,
            IRepository<AuditEvent> auditEventRepository,
            IAuthorizationService authorizationService,
            IUnitOfWork unitOfWork)
        {
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _roleRepository = roleRepository ?? throw new ArgumentNullException(nameof(roleRepository));
            _permissionRepository = permissionRepository ?? throw new ArgumentNullException(nameof(permissionRepository));
            _userRoleRepository = userRoleRepository ?? throw new ArgumentNullException(nameof(userRoleRepository));
            _rolePermissionRepository = rolePermissionRepository ?? throw new ArgumentNullException(nameof(rolePermissionRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<Role>> HandleAsync(CreateRoleCommand command, CancellationToken cancellationToken = default)
        {
            var authResult = await _authorizationService.AuthorizeAsync(command.ActingPrincipal, PermissionCatalog.ManageRoles, cancellationToken: cancellationToken);
            if (!authResult.IsAllowed)
            {
                return Result<Role>.Failure(authResult.ErrorCode ?? "FORBIDDEN", authResult.FailureReason ?? "Permission denied.");
            }

            if (string.IsNullOrWhiteSpace(command.Code))
            {
                return Result<Role>.Failure("INVALID_ROLE_CODE", "Role code is required.");
            }

            string normalizedCode = command.Code.Trim();
            var existing = await _roleRepository.FirstOrDefaultAsync(r => r.Code.ToLower() == normalizedCode.ToLower(), track: false, cancellationToken: cancellationToken);
            if (existing != null)
            {
                return Result<Role>.Failure("ROLE_ALREADY_EXISTS", $"Role with code '{normalizedCode}' already exists.");
            }

            var role = new Role
            {
                Code = normalizedCode,
                Name = string.IsNullOrWhiteSpace(command.Name) ? normalizedCode : command.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim(),
                Status = "Active",
                IsSystemRole = false
            };

            role.NormalizeAndValidate();

            await _roleRepository.AddAsync(role, cancellationToken);
            await RecordAuditEventAsync("ROLE_CREATED", command.ActingPrincipal, role.Code, role.Id.ToString(), cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return Result<Role>.Success(role);
        }

        public async Task<Result<bool>> HandleAsync(AssignRoleToUserCommand command, CancellationToken cancellationToken = default)
        {
            var authResult = await _authorizationService.AuthorizeAsync(command.ActingPrincipal, PermissionCatalog.ManageRoles, cancellationToken: cancellationToken);
            if (!authResult.IsAllowed)
            {
                return Result<bool>.Failure(authResult.ErrorCode ?? "FORBIDDEN", authResult.FailureReason ?? "Permission denied.");
            }

            var user = await _userRepository.GetByIdAsync(command.UserEntityId, track: true, cancellationToken: cancellationToken);
            if (user == null)
            {
                return Result<bool>.Failure("USER_NOT_FOUND", $"User with ID '{command.UserEntityId}' was not found.");
            }

            var role = await _roleRepository.FirstOrDefaultAsync(r => r.Code.ToLower() == command.RoleCode.Trim().ToLower(), track: false, cancellationToken: cancellationToken);
            if (role == null)
            {
                return Result<bool>.Failure("ROLE_NOT_FOUND", $"Role with code '{command.RoleCode}' was not found.");
            }

            if (!role.IsActive)
            {
                return Result<bool>.Failure("INVALID_ROLE_STATE", $"Role '{role.Code}' is disabled and cannot be assigned.");
            }

            var existingMapping = await _userRoleRepository.FirstOrDefaultAsync(ur => ur.UserEntityId == command.UserEntityId && ur.RoleId == role.Id, track: false, cancellationToken: cancellationToken);
            if (existingMapping != null)
            {
                return Result<bool>.Failure("ROLE_ALREADY_ASSIGNED", $"Role '{role.Code}' is already assigned to user '{command.UserEntityId}'.");
            }

            var userRole = new UserRoleEntity
            {
                UserEntityId = command.UserEntityId,
                RoleId = role.Id
            };

            await _userRoleRepository.AddAsync(userRole, cancellationToken);
            await RecordAuditEventAsync("ROLE_ASSIGNED", command.ActingPrincipal, role.Code, command.UserEntityId.ToString(), cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return Result<bool>.Success(true);
        }

        public async Task<Result<bool>> HandleAsync(RemoveRoleFromUserCommand command, CancellationToken cancellationToken = default)
        {
            var authResult = await _authorizationService.AuthorizeAsync(command.ActingPrincipal, PermissionCatalog.ManageRoles, cancellationToken: cancellationToken);
            if (!authResult.IsAllowed)
            {
                return Result<bool>.Failure(authResult.ErrorCode ?? "FORBIDDEN", authResult.FailureReason ?? "Permission denied.");
            }

            var role = await _roleRepository.FirstOrDefaultAsync(r => r.Code.ToLower() == command.RoleCode.Trim().ToLower(), track: false, cancellationToken: cancellationToken);
            if (role == null)
            {
                return Result<bool>.Failure("ROLE_NOT_FOUND", $"Role with code '{command.RoleCode}' was not found.");
            }

            var existingMapping = await _userRoleRepository.FirstOrDefaultAsync(ur => ur.UserEntityId == command.UserEntityId && ur.RoleId == role.Id, track: true, cancellationToken: cancellationToken);
            if (existingMapping != null)
            {
                _userRoleRepository.Delete(existingMapping);
                await RecordAuditEventAsync("ROLE_REMOVED", command.ActingPrincipal, role.Code, command.UserEntityId.ToString(), cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
            }

            return Result<bool>.Success(true);
        }

        public async Task<Result<bool>> HandleAsync(AssignPermissionToRoleCommand command, CancellationToken cancellationToken = default)
        {
            var authResult = await _authorizationService.AuthorizeAsync(command.ActingPrincipal, PermissionCatalog.ManagePermissions, cancellationToken: cancellationToken);
            if (!authResult.IsAllowed)
            {
                return Result<bool>.Failure(authResult.ErrorCode ?? "FORBIDDEN", authResult.FailureReason ?? "Permission denied.");
            }

            var role = await _roleRepository.FirstOrDefaultAsync(r => r.Code.ToLower() == command.RoleCode.Trim().ToLower(), track: false, cancellationToken: cancellationToken);
            if (role == null)
            {
                return Result<bool>.Failure("ROLE_NOT_FOUND", $"Role with code '{command.RoleCode}' was not found.");
            }

            var perm = await _permissionRepository.FirstOrDefaultAsync(p => p.Code.ToLower() == command.PermissionCode.Trim().ToLower(), track: false, cancellationToken: cancellationToken);
            if (perm == null)
            {
                return Result<bool>.Failure("PERMISSION_NOT_FOUND", $"Permission with code '{command.PermissionCode}' was not found.");
            }

            if (!perm.IsActive)
            {
                return Result<bool>.Failure("INVALID_PERMISSION_STATE", $"Permission '{perm.Code}' is disabled and cannot be assigned.");
            }

            var existing = await _rolePermissionRepository.FirstOrDefaultAsync(rp => rp.RoleId == role.Id && rp.PermissionId == perm.Id, track: false, cancellationToken: cancellationToken);
            if (existing != null)
            {
                return Result<bool>.Failure("PERMISSION_ALREADY_ASSIGNED", $"Permission '{perm.Code}' is already assigned to role '{role.Code}'.");
            }

            var rolePerm = new RolePermission
            {
                RoleId = role.Id,
                PermissionId = perm.Id
            };

            await _rolePermissionRepository.AddAsync(rolePerm, cancellationToken);
            await RecordAuditEventAsync("PERMISSION_ASSIGNED", command.ActingPrincipal, perm.Code, role.Code, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return Result<bool>.Success(true);
        }

        public async Task<Result<bool>> HandleAsync(RemovePermissionFromRoleCommand command, CancellationToken cancellationToken = default)
        {
            var authResult = await _authorizationService.AuthorizeAsync(command.ActingPrincipal, PermissionCatalog.ManagePermissions, cancellationToken: cancellationToken);
            if (!authResult.IsAllowed)
            {
                return Result<bool>.Failure(authResult.ErrorCode ?? "FORBIDDEN", authResult.FailureReason ?? "Permission denied.");
            }

            var role = await _roleRepository.FirstOrDefaultAsync(r => r.Code.ToLower() == command.RoleCode.Trim().ToLower(), track: false, cancellationToken: cancellationToken);
            if (role == null)
            {
                return Result<bool>.Failure("ROLE_NOT_FOUND", $"Role with code '{command.RoleCode}' was not found.");
            }

            var perm = await _permissionRepository.FirstOrDefaultAsync(p => p.Code.ToLower() == command.PermissionCode.Trim().ToLower(), track: false, cancellationToken: cancellationToken);
            if (perm == null)
            {
                return Result<bool>.Failure("PERMISSION_NOT_FOUND", $"Permission with code '{command.PermissionCode}' was not found.");
            }

            var existing = await _rolePermissionRepository.FirstOrDefaultAsync(rp => rp.RoleId == role.Id && rp.PermissionId == perm.Id, track: true, cancellationToken: cancellationToken);
            if (existing != null)
            {
                _rolePermissionRepository.Delete(existing);
                await RecordAuditEventAsync("PERMISSION_REMOVED", command.ActingPrincipal, perm.Code, role.Code, cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
            }

            return Result<bool>.Success(true);
        }

        public async Task<Result<bool>> HandleAsync(DisableRoleCommand command, CancellationToken cancellationToken = default)
        {
            var authResult = await _authorizationService.AuthorizeAsync(command.ActingPrincipal, PermissionCatalog.ManageRoles, cancellationToken: cancellationToken);
            if (!authResult.IsAllowed)
            {
                return Result<bool>.Failure(authResult.ErrorCode ?? "FORBIDDEN", authResult.FailureReason ?? "Permission denied.");
            }

            var role = await _roleRepository.FirstOrDefaultAsync(r => r.Code.ToLower() == command.RoleCode.Trim().ToLower(), track: true, cancellationToken: cancellationToken);
            if (role == null)
            {
                return Result<bool>.Failure("ROLE_NOT_FOUND", $"Role with code '{command.RoleCode}' was not found.");
            }

            role.Disable();
            await RecordAuditEventAsync("ROLE_DISABLED", command.ActingPrincipal, role.Code, role.Id.ToString(), cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return Result<bool>.Success(true);
        }

        public async Task<Result<bool>> HandleAsync(DisablePermissionCommand command, CancellationToken cancellationToken = default)
        {
            var authResult = await _authorizationService.AuthorizeAsync(command.ActingPrincipal, PermissionCatalog.ManagePermissions, cancellationToken: cancellationToken);
            if (!authResult.IsAllowed)
            {
                return Result<bool>.Failure(authResult.ErrorCode ?? "FORBIDDEN", authResult.FailureReason ?? "Permission denied.");
            }

            var perm = await _permissionRepository.FirstOrDefaultAsync(p => p.Code.ToLower() == command.PermissionCode.Trim().ToLower(), track: true, cancellationToken: cancellationToken);
            if (perm == null)
            {
                return Result<bool>.Failure("PERMISSION_NOT_FOUND", $"Permission with code '{command.PermissionCode}' was not found.");
            }

            perm.Disable();
            await RecordAuditEventAsync("PERMISSION_DISABLED", command.ActingPrincipal, perm.Code, perm.Id.ToString(), cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return Result<bool>.Success(true);
        }

        public async Task<Result<List<Role>>> HandleAsync(GetRolesQuery query, CancellationToken cancellationToken = default)
        {
            var roles = await _roleRepository.GetAllAsync(track: false, cancellationToken: cancellationToken);
            return Result<List<Role>>.Success(roles.ToList());
        }

        public async Task<Result<List<Role>>> HandleAsync(GetUserRolesQuery query, CancellationToken cancellationToken = default)
        {
            var userRoles = await _userRoleRepository.FindAsync(ur => ur.UserEntityId == query.UserEntityId, track: false, cancellationToken: cancellationToken);
            var roleIds = userRoles.Select(ur => ur.RoleId).ToList();

            if (!roleIds.Any())
            {
                return Result<List<Role>>.Success(new List<Role>());
            }

            var roles = await _roleRepository.FindAsync(r => roleIds.Contains(r.Id), track: false, cancellationToken: cancellationToken);
            return Result<List<Role>>.Success(roles.ToList());
        }

        public async Task<Result<List<Permission>>> HandleAsync(GetRolePermissionsQuery query, CancellationToken cancellationToken = default)
        {
            var role = await _roleRepository.FirstOrDefaultAsync(r => r.Code.ToLower() == query.RoleCode.Trim().ToLower(), track: false, cancellationToken: cancellationToken);
            if (role == null)
            {
                return Result<List<Permission>>.Failure("ROLE_NOT_FOUND", $"Role with code '{query.RoleCode}' was not found.");
            }

            var rolePerms = await _rolePermissionRepository.FindAsync(rp => rp.RoleId == role.Id, track: false, cancellationToken: cancellationToken);
            var permIds = rolePerms.Select(rp => rp.PermissionId).ToList();

            if (!permIds.Any())
            {
                return Result<List<Permission>>.Success(new List<Permission>());
            }

            var perms = await _permissionRepository.FindAsync(p => permIds.Contains(p.Id), track: false, cancellationToken: cancellationToken);
            return Result<List<Permission>>.Success(perms.ToList());
        }

        public async Task<Result<List<Permission>>> HandleAsync(GetPermissionsQuery query, CancellationToken cancellationToken = default)
        {
            var perms = await _permissionRepository.GetAllAsync(track: false, cancellationToken: cancellationToken);
            return Result<List<Permission>>.Success(perms.ToList());
        }

        public async Task<Result<List<string>>> HandleAsync(GetUserPermissionsQuery query, CancellationToken cancellationToken = default)
        {
            var userRoles = await _userRoleRepository.FindAsync(ur => ur.UserEntityId == query.UserEntityId, track: false, cancellationToken: cancellationToken);
            var roleIds = userRoles.Select(ur => ur.RoleId).ToList();

            if (!roleIds.Any())
            {
                return Result<List<string>>.Success(new List<string>());
            }

            // Direct string comparison ("Active") allows EF Core to utilize PostgreSQL B-tree indexes on Status, avoiding lower() function wrap overhead in database queries.
            var activeRoles = await _roleRepository.FindAsync(r => roleIds.Contains(r.Id) && r.Status == "Active", track: false, cancellationToken: cancellationToken);
            var activeRoleIds = activeRoles.Select(r => r.Id).ToList();

            if (!activeRoleIds.Any())
            {
                return Result<List<string>>.Success(new List<string>());
            }

            var rolePermissions = await _rolePermissionRepository.FindAsync(rp => activeRoleIds.Contains(rp.RoleId), track: false, cancellationToken: cancellationToken);
            var permIds = rolePermissions.Select(rp => rp.PermissionId).Distinct().ToList();

            if (!permIds.Any())
            {
                return Result<List<string>>.Success(new List<string>());
            }

            var activePermissions = await _permissionRepository.FindAsync(p => permIds.Contains(p.Id) && p.Status == "Active", track: false, cancellationToken: cancellationToken);
            var permCodes = activePermissions.Select(p => p.Code).Distinct().ToList();

            return Result<List<string>>.Success(permCodes);
        }

        public async Task<Result<bool>> HandleAsync(CheckPermissionQuery query, CancellationToken cancellationToken = default)
        {
            var userPermissionsResult = await HandleAsync(new GetUserPermissionsQuery { UserEntityId = query.UserEntityId }, cancellationToken);
            if (!userPermissionsResult.IsSuccess)
            {
                return Result<bool>.Failure(userPermissionsResult.ErrorCode ?? "FAILURE", userPermissionsResult.ErrorMessage);
            }

            bool hasPerm = userPermissionsResult.Value != null && userPermissionsResult.Value.Any(p => string.Equals(p, query.PermissionCode, StringComparison.OrdinalIgnoreCase));
            return Result<bool>.Success(hasPerm);
        }

        private async Task RecordAuditEventAsync(
            string eventType,
            UserPrincipal principal,
            string target,
            string targetId,
            CancellationToken cancellationToken)
        {
            try
            {
                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = eventType,
                    CorrelationId = principal.UserId?.ToString() ?? principal.GamerId?.ToString() ?? "ANONYMOUS",
                    Payload = ProtocolSerialization.Serialize(new
                    {
                        actor = principal.Username ?? "ANONYMOUS",
                        action = eventType,
                        target = target,
                        targetId = targetId,
                        timestamp = DateTime.UtcNow
                    }),
                    Timestamp = DateTime.UtcNow
                };

                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);
            }
            catch
            {
                // Non-blocking audit recording
            }
        }
    }
}

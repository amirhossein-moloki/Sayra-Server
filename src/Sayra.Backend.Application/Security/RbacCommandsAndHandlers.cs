using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Security
{
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

    public class GetUserPermissionsQuery : IQuery<List<string>>
    {
        public Guid UserEntityId { get; set; }
    }

    public class RbacHandlers :
        ICommandHandler<AssignRoleToUserCommand, bool>,
        ICommandHandler<RemoveRoleFromUserCommand, bool>,
        ICommandHandler<AssignPermissionToRoleCommand, bool>,
        ICommandHandler<RemovePermissionFromRoleCommand, bool>,
        IQueryHandler<GetUserPermissionsQuery, List<string>>
    {
        private readonly IRepository<User> _userRepository;
        private readonly IRepository<Role> _roleRepository;
        private readonly IRepository<Permission> _permissionRepository;
        private readonly IRepository<UserRoleEntity> _userRoleRepository;
        private readonly IRepository<RolePermission> _rolePermissionRepository;
        private readonly IAuthorizationService _authorizationService;
        private readonly IUnitOfWork _unitOfWork;

        public RbacHandlers(
            IRepository<User> userRepository,
            IRepository<Role> roleRepository,
            IRepository<Permission> permissionRepository,
            IRepository<UserRoleEntity> userRoleRepository,
            IRepository<RolePermission> rolePermissionRepository,
            IAuthorizationService authorizationService,
            IUnitOfWork unitOfWork)
        {
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _roleRepository = roleRepository ?? throw new ArgumentNullException(nameof(roleRepository));
            _permissionRepository = permissionRepository ?? throw new ArgumentNullException(nameof(permissionRepository));
            _userRoleRepository = userRoleRepository ?? throw new ArgumentNullException(nameof(userRoleRepository));
            _rolePermissionRepository = rolePermissionRepository ?? throw new ArgumentNullException(nameof(rolePermissionRepository));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
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

            var existingMapping = await _userRoleRepository.FirstOrDefaultAsync(ur => ur.UserEntityId == command.UserEntityId && ur.RoleId == role.Id, track: false, cancellationToken: cancellationToken);
            if (existingMapping != null)
            {
                return Result<bool>.Success(true); // Idempotent success
            }

            var userRole = new UserRoleEntity
            {
                UserEntityId = command.UserEntityId,
                RoleId = role.Id
            };

            await _userRoleRepository.AddAsync(userRole, cancellationToken);
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

            var existing = await _rolePermissionRepository.FirstOrDefaultAsync(rp => rp.RoleId == role.Id && rp.PermissionId == perm.Id, track: false, cancellationToken: cancellationToken);
            if (existing != null)
            {
                return Result<bool>.Success(true);
            }

            var rolePerm = new RolePermission
            {
                RoleId = role.Id,
                PermissionId = perm.Id
            };

            await _rolePermissionRepository.AddAsync(rolePerm, cancellationToken);
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
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
            }

            return Result<bool>.Success(true);
        }

        public async Task<Result<List<string>>> HandleAsync(GetUserPermissionsQuery query, CancellationToken cancellationToken = default)
        {
            var userRoles = await _userRoleRepository.FindAsync(ur => ur.UserEntityId == query.UserEntityId, track: false, cancellationToken: cancellationToken);
            var roleIds = userRoles.Select(ur => ur.RoleId).ToList();

            if (!roleIds.Any())
            {
                return Result<List<string>>.Success(new List<string>());
            }

            var rolePermissions = await _rolePermissionRepository.FindAsync(rp => roleIds.Contains(rp.RoleId), track: false, cancellationToken: cancellationToken);
            var permIds = rolePermissions.Select(rp => rp.PermissionId).Distinct().ToList();

            var permissions = await _permissionRepository.FindAsync(p => permIds.Contains(p.Id), track: false, cancellationToken: cancellationToken);
            var permCodes = permissions.Select(p => p.Code).Distinct().ToList();

            return Result<List<string>>.Success(permCodes);
        }
    }
}

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Events;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Gamers
{
    public class CreateGamerCommandHandler : ICommandHandler<CreateGamerCommand, Gamer>
    {
        private readonly IRepository<Gamer> _gamerRepository;
        private readonly IRepository<GamerCredential> _gamerCredentialRepository;
        private readonly IRepository<GamerAccount> _gamerAccountRepository;
        private readonly IRepository<User> _userRepository;
        private readonly IRepository<UserCredential> _userCredentialRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IUnitOfWork _unitOfWork;

        public CreateGamerCommandHandler(
            IRepository<Gamer> gamerRepository,
            IRepository<GamerCredential> gamerCredentialRepository,
            IRepository<GamerAccount> gamerAccountRepository,
            IRepository<User> userRepository,
            IRepository<UserCredential> userCredentialRepository,
            IRepository<AuditEvent> auditEventRepository,
            IPasswordHasher passwordHasher,
            IUnitOfWork unitOfWork)
        {
            _gamerRepository = gamerRepository ?? throw new ArgumentNullException(nameof(gamerRepository));
            _gamerCredentialRepository = gamerCredentialRepository ?? throw new ArgumentNullException(nameof(gamerCredentialRepository));
            _gamerAccountRepository = gamerAccountRepository ?? throw new ArgumentNullException(nameof(gamerAccountRepository));
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _userCredentialRepository = userCredentialRepository ?? throw new ArgumentNullException(nameof(userCredentialRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<Gamer>> HandleAsync(CreateGamerCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new CreateGamerCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<Gamer>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var usernameLower = command.Username.Trim().ToLowerInvariant();
                var existingUsername = await _gamerRepository.FirstOrDefaultAsync(g => g.Username.ToLower() == usernameLower, track: false, cancellationToken);
                if (existingUsername != null)
                {
                    return Result<Gamer>.Failure("DUPLICATE_USERNAME", $"Username '{command.Username}' is already taken.");
                }

                var existingUser = await _userRepository.FirstOrDefaultAsync(u => u.Username.ToLower() == usernameLower, track: false, cancellationToken);
                if (existingUser != null)
                {
                    return Result<Gamer>.Failure("DUPLICATE_USERNAME", $"Username '{command.Username}' is already taken.");
                }

                var emailLower = command.Email.Trim().ToLowerInvariant();
                var existingEmail = await _gamerRepository.FirstOrDefaultAsync(g => g.Email.ToLower() == emailLower, track: false, cancellationToken);
                if (existingEmail != null)
                {
                    return Result<Gamer>.Failure("DUPLICATE_EMAIL", $"Email '{command.Email}' is already in use.");
                }

                var gamer = new Gamer
                {
                    Username = command.Username,
                    Email = command.Email,
                    PhoneNumber = command.PhoneNumber,
                    FirstName = command.FirstName,
                    LastName = command.LastName,
                    BirthDate = command.BirthDate,
                    OrganizationEntityId = command.OrganizationId,
                    SiteEntityId = command.SiteId,
                    Status = "Active"
                };

                gamer.NormalizeAndValidate();
                await _gamerRepository.AddAsync(gamer, cancellationToken);

                // Create associated User identity record
                var user = new User
                {
                    Username = command.Username,
                    DisplayName = string.IsNullOrWhiteSpace(command.FirstName) && string.IsNullOrWhiteSpace(command.LastName)
                        ? command.Username
                        : $"{command.FirstName} {command.LastName}".Trim(),
                    Email = command.Email,
                    PhoneNumber = command.PhoneNumber,
                    Role = UserRole.Gamer,
                    Status = UserAccountState.Active,
                    GamerEntityId = gamer.Id
                };

                user.NormalizeAndValidate();
                await _userRepository.AddAsync(user, cancellationToken);

                // Hash password with Argon2id and store in UserCredential entity
                var (hash, salt, algo, parameters) = _passwordHasher.HashPasswordWithDetails(command.Password);
                var userCredential = new UserCredential
                {
                    UserEntityId = user.Id,
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    HashAlgorithm = algo,
                    HashParameters = parameters,
                    LastPasswordChangedAt = DateTime.UtcNow
                };

                await _userCredentialRepository.AddAsync(userCredential, cancellationToken);

                // Store in GamerCredential entity for backward compatibility
                var gamerCredential = new GamerCredential
                {
                    GamerEntityId = gamer.Id,
                    CredentialType = "Password",
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    HashAlgorithm = algo,
                    FailedAttemptCount = 0,
                    IsLocked = false,
                    LastPasswordChangedAt = DateTime.UtcNow
                };

                await _gamerCredentialRepository.AddAsync(gamerCredential, cancellationToken);

                // Create associated GamerAccount integration point
                var account = new GamerAccount
                {
                    GamerEntityId = gamer.Id,
                    Status = "Active",
                    Currency = "SAY",
                    Balance = 0.00m,
                    BonusBalance = 0.00m
                };

                account.NormalizeAndValidate();
                await _gamerAccountRepository.AddAsync(account, cancellationToken);

                // Record Audit Events
                var gamerCreatedAudit = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(GamerCreated),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(new GamerCreated(
                        gamer.Id,
                        gamer.GamerId,
                        gamer.Username,
                        gamer.Email,
                        gamer.Status,
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(gamerCreatedAudit, cancellationToken);

                var accountCreatedAudit = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(GamerAccountCreated),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(new GamerAccountCreated(
                        account.Id,
                        gamer.Id,
                        account.AccountNumber,
                        account.Status,
                        account.Balance,
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(accountCreatedAudit, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<Gamer>.Success(gamer);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<Gamer>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<Gamer>.Failure("CREATE_GAMER_FAILED", ex.Message);
            }
        }
    }

    public class UpdateGamerProfileCommandHandler : ICommandHandler<UpdateGamerProfileCommand, Gamer>
    {
        private readonly IRepository<Gamer> _gamerRepository;
        private readonly IRepository<User> _userRepository;
        private readonly IUnitOfWork _unitOfWork;

        public UpdateGamerProfileCommandHandler(
            IRepository<Gamer> gamerRepository,
            IRepository<User> userRepository,
            IUnitOfWork unitOfWork)
        {
            _gamerRepository = gamerRepository ?? throw new ArgumentNullException(nameof(gamerRepository));
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<Gamer>> HandleAsync(UpdateGamerProfileCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new UpdateGamerProfileCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<Gamer>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var gamer = await _gamerRepository.GetByIdAsync(command.GamerEntityId, track: true, cancellationToken);
                if (gamer == null)
                {
                    return Result<Gamer>.Failure("NOT_FOUND", $"Gamer with ID '{command.GamerEntityId}' not found.");
                }

                if (!string.IsNullOrWhiteSpace(command.Email))
                {
                    var newEmailLower = command.Email.Trim().ToLowerInvariant();
                    if (!gamer.Email.Equals(newEmailLower, StringComparison.OrdinalIgnoreCase))
                    {
                        var existingEmail = await _gamerRepository.FirstOrDefaultAsync(g => g.Email.ToLower() == newEmailLower && g.Id != gamer.Id, track: false, cancellationToken);
                        if (existingEmail != null)
                        {
                            return Result<Gamer>.Failure("DUPLICATE_EMAIL", $"Email '{command.Email}' is already in use by another gamer.");
                        }
                        gamer.Email = command.Email;
                    }
                }

                if (command.PhoneNumber != null) gamer.PhoneNumber = command.PhoneNumber;
                if (command.FirstName != null) gamer.FirstName = command.FirstName;
                if (command.LastName != null) gamer.LastName = command.LastName;
                if (command.BirthDate.HasValue) gamer.BirthDate = command.BirthDate;

                gamer.NormalizeAndValidate();
                _gamerRepository.Update(gamer);

                // Sync with associated User entity if present
                var user = await _userRepository.FirstOrDefaultAsync(u => u.GamerEntityId == gamer.Id, track: true, cancellationToken);
                if (user != null)
                {
                    user.Email = gamer.Email;
                    user.PhoneNumber = gamer.PhoneNumber;
                    user.DisplayName = $"{gamer.FirstName} {gamer.LastName}".Trim();
                    user.NormalizeAndValidate();
                    _userRepository.Update(user);
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<Gamer>.Success(gamer);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<Gamer>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<Gamer>.Failure("UPDATE_GAMER_FAILED", ex.Message);
            }
        }
    }

    public class DeactivateGamerCommandHandler : ICommandHandler<DeactivateGamerCommand, Gamer>
    {
        private readonly IRepository<Gamer> _gamerRepository;
        private readonly IRepository<User> _userRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public DeactivateGamerCommandHandler(
            IRepository<Gamer> gamerRepository,
            IRepository<User> userRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _gamerRepository = gamerRepository ?? throw new ArgumentNullException(nameof(gamerRepository));
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<Gamer>> HandleAsync(DeactivateGamerCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new DeactivateGamerCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<Gamer>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var gamer = await _gamerRepository.GetByIdAsync(command.GamerEntityId, track: true, cancellationToken);
                if (gamer == null)
                {
                    return Result<Gamer>.Failure("NOT_FOUND", $"Gamer with ID '{command.GamerEntityId}' not found.");
                }

                gamer.Deactivate();
                _gamerRepository.Update(gamer);

                var user = await _userRepository.FirstOrDefaultAsync(u => u.GamerEntityId == gamer.Id, track: true, cancellationToken);
                if (user != null)
                {
                    user.TransitionTo(UserAccountState.Disabled);
                    _userRepository.Update(user);
                }

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(GamerDeactivated),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(new GamerDeactivated(
                        gamer.Id,
                        gamer.GamerId,
                        gamer.Username,
                        gamer.Status,
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<Gamer>.Success(gamer);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<Gamer>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<Gamer>.Failure("DEACTIVATE_GAMER_FAILED", ex.Message);
            }
        }
    }

    public class ChangeGamerPasswordCommandHandler : ICommandHandler<ChangeGamerPasswordCommand, bool>
    {
        private readonly IRepository<GamerCredential> _gamerCredentialRepository;
        private readonly IRepository<User> _userRepository;
        private readonly IRepository<UserCredential> _userCredentialRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ISecurityEventService? _securityEventService;
        private readonly IAuthenticationSessionService? _authenticationSessionService;
        private readonly IUnitOfWork _unitOfWork;

        public ChangeGamerPasswordCommandHandler(
            IRepository<GamerCredential> gamerCredentialRepository,
            IRepository<User> userRepository,
            IRepository<UserCredential> userCredentialRepository,
            IRepository<AuditEvent> auditEventRepository,
            IPasswordHasher passwordHasher,
            IUnitOfWork unitOfWork,
            ISecurityEventService? securityEventService = null,
            IAuthenticationSessionService? authenticationSessionService = null)
        {
            _gamerCredentialRepository = gamerCredentialRepository ?? throw new ArgumentNullException(nameof(gamerCredentialRepository));
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _userCredentialRepository = userCredentialRepository ?? throw new ArgumentNullException(nameof(userCredentialRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _securityEventService = securityEventService;
            _authenticationSessionService = authenticationSessionService;
        }

        public async Task<Result<bool>> HandleAsync(ChangeGamerPasswordCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new ChangeGamerPasswordCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<bool>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
                }

                var gamerCredential = await _gamerCredentialRepository.FirstOrDefaultAsync(c => c.GamerEntityId == command.GamerEntityId, track: true, cancellationToken);
                var user = await _userRepository.FirstOrDefaultAsync(u => u.GamerEntityId == command.GamerEntityId, track: true, cancellationToken);
                UserCredential? userCredential = null;
                if (user != null)
                {
                    userCredential = await _userCredentialRepository.FirstOrDefaultAsync(uc => uc.UserEntityId == user.Id, track: true, cancellationToken);
                }

                if (gamerCredential == null && userCredential == null)
                {
                    return Result<bool>.Failure("NOT_FOUND", $"Gamer credentials for ID '{command.GamerEntityId}' not found.");
                }

                string hash = userCredential?.PasswordHash ?? gamerCredential?.PasswordHash ?? string.Empty;
                string salt = userCredential?.PasswordSalt ?? gamerCredential?.PasswordSalt ?? string.Empty;
                string algo = userCredential?.HashAlgorithm ?? gamerCredential?.HashAlgorithm ?? "Argon2id";

                if (!_passwordHasher.VerifyPassword(command.CurrentPassword, hash, salt, algo))
                {
                    return Result<bool>.Failure("INVALID_CREDENTIALS", "Current password verification failed.");
                }

                var (newHash, newSalt, newAlgo, newParams) = _passwordHasher.HashPasswordWithDetails(command.NewPassword);

                if (gamerCredential != null)
                {
                    gamerCredential.SetPassword(newHash, newSalt, newAlgo);
                    _gamerCredentialRepository.Update(gamerCredential);
                }

                if (userCredential != null)
                {
                    userCredential.SetPassword(newHash, newSalt, newAlgo, newParams);
                    _userCredentialRepository.Update(userCredential);
                }

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = nameof(GamerPasswordChanged),
                    EventVersion = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(new GamerPasswordChanged(
                        command.GamerEntityId,
                        command.GamerEntityId.ToString(),
                        DateTime.UtcNow
                    ))
                };
                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);

                if (_securityEventService != null)
                {
                    await _securityEventService.RecordSecurityEventAsync(
                        eventType: "PASSWORD_CHANGED",
                        actorId: command.GamerEntityId,
                        actorType: "Gamer",
                        deviceId: null,
                        organizationId: null,
                        siteId: null,
                        resourceType: "Account",
                        resourceId: command.GamerEntityId,
                        action: "CHANGE_PASSWORD",
                        result: "SUCCESS",
                        failureReason: null,
                        cancellationToken: cancellationToken);
                }

                if (_authenticationSessionService != null)
                {
                    if (user != null)
                    {
                        await _authenticationSessionService.RevokeAllUserSessionsAsync(user.Id, "PASSWORD_CHANGED", cancellationToken);
                    }
                    await _authenticationSessionService.RevokeAllGamerSessionsAsync(command.GamerEntityId, "PASSWORD_CHANGED", cancellationToken);
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<bool>.Success(true);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<bool>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<bool>.Failure("CHANGE_PASSWORD_FAILED", ex.Message);
            }
        }
    }

    public class AuthenticateGamerCommandHandler : ICommandHandler<AuthenticateGamerCommand, AuthenticateGamerResponseDto>
    {
        private readonly IRepository<Gamer> _gamerRepository;
        private readonly IRepository<GamerCredential> _gamerCredentialRepository;
        private readonly IRepository<User> _userRepository;
        private readonly IRepository<UserCredential> _userCredentialRepository;
        private readonly IRepository<GamerAccount> _gamerAccountRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ILoginProtectionService _loginProtectionService;
        private readonly ISecurityEventService _securityEventService;
        private readonly IAuthenticationSessionService? _authenticationSessionService;
        private readonly IUnitOfWork _unitOfWork;

        public AuthenticateGamerCommandHandler(
            IRepository<Gamer> gamerRepository,
            IRepository<GamerCredential> gamerCredentialRepository,
            IRepository<User> userRepository,
            IRepository<UserCredential> userCredentialRepository,
            IRepository<GamerAccount> gamerAccountRepository,
            IPasswordHasher passwordHasher,
            ILoginProtectionService loginProtectionService,
            ISecurityEventService securityEventService,
            IUnitOfWork unitOfWork,
            IAuthenticationSessionService? authenticationSessionService = null)
        {
            _gamerRepository = gamerRepository ?? throw new ArgumentNullException(nameof(gamerRepository));
            _gamerCredentialRepository = gamerCredentialRepository ?? throw new ArgumentNullException(nameof(gamerCredentialRepository));
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _userCredentialRepository = userCredentialRepository ?? throw new ArgumentNullException(nameof(userCredentialRepository));
            _gamerAccountRepository = gamerAccountRepository ?? throw new ArgumentNullException(nameof(gamerAccountRepository));
            _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
            _loginProtectionService = loginProtectionService ?? throw new ArgumentNullException(nameof(loginProtectionService));
            _securityEventService = securityEventService ?? throw new ArgumentNullException(nameof(securityEventService));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _authenticationSessionService = authenticationSessionService;
        }

        public async Task<Result<AuthenticateGamerResponseDto>> HandleAsync(AuthenticateGamerCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var validator = new AuthenticateGamerCommandValidator();
                var validationResult = await validator.ValidateAsync(command, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var firstError = validationResult.Errors.First();
                    return Result<AuthenticateGamerResponseDto>.Success(new AuthenticateGamerResponseDto
                    {
                        IsSuccess = false,
                        ErrorCode = firstError.ErrorCode ?? "VALIDATION_FAILED",
                        ErrorMessage = firstError.ErrorMessage
                    });
                }

                var inputLower = command.UsernameOrEmail.Trim().ToLowerInvariant();

                if (await _loginProtectionService.IsLockedOutAsync(inputLower, cancellationToken))
                {
                    await _securityEventService.RecordSecurityEventAsync(
                        eventType: "ACCOUNT_LOCKED",
                        actorId: null,
                        actorType: "ANONYMOUS",
                        deviceId: null,
                        organizationId: null,
                        siteId: null,
                        resourceType: "Account",
                        resourceId: null,
                        action: "LOGIN",
                        result: "LOCKED",
                        failureReason: "Login blocked due to active account lockout.",
                        cancellationToken: cancellationToken);

                    return Result<AuthenticateGamerResponseDto>.Success(new AuthenticateGamerResponseDto
                    {
                        IsSuccess = false,
                        ErrorCode = "ACCOUNT_LOCKED",
                        ErrorMessage = "Account is temporarily locked due to too many failed login attempts."
                    });
                }

                // Lookup User and Gamer records
                var user = await _userRepository.FirstOrDefaultAsync(u => u.Username.ToLower() == inputLower || (u.Email != null && u.Email.ToLower() == inputLower), track: true, cancellationToken);
                var gamer = await _gamerRepository.FirstOrDefaultAsync(g => g.Username.ToLower() == inputLower || (g.Email != null && g.Email.ToLower() == inputLower), track: true, cancellationToken);

                if (user == null && gamer == null)
                {
                    await _loginProtectionService.RecordFailedAttemptAsync(inputLower, null, failureReason: "User not found", cancellationToken: cancellationToken);

                    return Result<AuthenticateGamerResponseDto>.Success(new AuthenticateGamerResponseDto
                    {
                        IsSuccess = false,
                        ErrorCode = "INVALID_CREDENTIALS",
                        ErrorMessage = "Invalid username/email or password."
                    });
                }

                if (user != null)
                {
                    if (user.Status == UserAccountState.Disabled || user.Status == UserAccountState.Suspended)
                    {
                        return Result<AuthenticateGamerResponseDto>.Success(new AuthenticateGamerResponseDto
                        {
                            IsSuccess = false,
                            ErrorCode = "ACCOUNT_DISABLED",
                            ErrorMessage = $"User account is currently {user.Status}."
                        });
                    }

                    if (user.IsCurrentlyLockedOut())
                    {
                        return Result<AuthenticateGamerResponseDto>.Success(new AuthenticateGamerResponseDto
                        {
                            IsSuccess = false,
                            ErrorCode = "ACCOUNT_LOCKED",
                            ErrorMessage = "Account is temporarily locked due to too many failed login attempts."
                        });
                    }
                }
                else if (gamer != null)
                {
                    if (!gamer.CanOperate())
                    {
                        return Result<AuthenticateGamerResponseDto>.Success(new AuthenticateGamerResponseDto
                        {
                            IsSuccess = false,
                            ErrorCode = "ACCOUNT_DISABLED",
                            ErrorMessage = $"Gamer account is currently {gamer.Status}."
                        });
                    }
                }

                // Retrieve credentials
                UserCredential? userCredential = null;
                GamerCredential? gamerCredential = null;

                if (user != null)
                {
                    userCredential = await _userCredentialRepository.FirstOrDefaultAsync(uc => uc.UserEntityId == user.Id, track: true, cancellationToken);
                }

                if (gamer != null)
                {
                    gamerCredential = await _gamerCredentialRepository.FirstOrDefaultAsync(gc => gc.GamerEntityId == gamer.Id, track: true, cancellationToken);
                }

                string? hash = userCredential?.PasswordHash ?? gamerCredential?.PasswordHash;
                string? salt = userCredential?.PasswordSalt ?? gamerCredential?.PasswordSalt;
                string algo = userCredential?.HashAlgorithm ?? gamerCredential?.HashAlgorithm ?? "Argon2id";

                if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt))
                {
                    await _loginProtectionService.RecordFailedAttemptAsync(inputLower, user?.Id ?? gamer?.Id, failureReason: "Missing credentials", cancellationToken: cancellationToken);

                    return Result<AuthenticateGamerResponseDto>.Success(new AuthenticateGamerResponseDto
                    {
                        IsSuccess = false,
                        ErrorCode = "INVALID_CREDENTIALS",
                        ErrorMessage = "Invalid username/email or password."
                    });
                }

                bool isPasswordValid = _passwordHasher.VerifyPassword(command.Password, hash, salt, algo);
                if (!isPasswordValid)
                {
                    await _loginProtectionService.RecordFailedAttemptAsync(inputLower, user?.Id ?? gamer?.Id, failureReason: "Invalid credentials", cancellationToken: cancellationToken);

                    return Result<AuthenticateGamerResponseDto>.Success(new AuthenticateGamerResponseDto
                    {
                        IsSuccess = false,
                        ErrorCode = "INVALID_CREDENTIALS",
                        ErrorMessage = "Invalid username/email or password."
                    });
                }

                // Successful login -> Reset failed attempt counters and record login
                await _loginProtectionService.ResetAttemptsAsync(inputLower, user?.Id ?? gamer?.Id, cancellationToken);

                await _securityEventService.RecordSecurityEventAsync(
                    eventType: "LOGIN_SUCCESS",
                    actorId: user?.Id ?? gamer?.Id,
                    actorType: user != null ? "User" : "Gamer",
                    deviceId: null,
                    organizationId: user?.OrganizationEntityId,
                    siteId: user?.SiteEntityId,
                    resourceType: "Account",
                    resourceId: user?.Id ?? gamer?.Id,
                    action: "LOGIN",
                    result: "SUCCESS",
                    failureReason: null,
                    cancellationToken: cancellationToken);

                if (user != null)
                {
                    user.RecordSuccessfulLogin();
                    _userRepository.Update(user);
                }

                if (gamerCredential != null)
                {
                    gamerCredential.ResetFailedAttempts();
                    _gamerCredentialRepository.Update(gamerCredential);
                }

                // Automatic password rehash upgrade if algorithm or parameters need rehash
                if (_passwordHasher.NeedsRehash(algo, userCredential?.HashParameters))
                {
                    var (newHash, newSalt, newAlgo, newParams) = _passwordHasher.HashPasswordWithDetails(command.Password);

                    if (userCredential != null)
                    {
                        userCredential.SetPassword(newHash, newSalt, newAlgo, newParams);
                        _userCredentialRepository.Update(userCredential);
                    }

                    if (gamerCredential != null)
                    {
                        gamerCredential.SetPassword(newHash, newSalt, newAlgo);
                        _gamerCredentialRepository.Update(gamerCredential);
                    }
                }

                Guid targetGamerId = gamer?.Id ?? user?.GamerEntityId ?? user?.Id ?? Guid.Empty;
                string businessId = gamer?.GamerId ?? user?.UserId ?? string.Empty;
                string username = user?.Username ?? gamer?.Username ?? string.Empty;

                var account = await _gamerAccountRepository.FirstOrDefaultAsync(a => a.GamerEntityId == targetGamerId, track: false, cancellationToken);

                string? sessionToken = null;
                if (_authenticationSessionService != null)
                {
                    var authSession = await _authenticationSessionService.CreateSessionAsync(
                        userId: user?.Id,
                        gamerId: targetGamerId,
                        pcId: null,
                        deviceId: null,
                        lifetime: TimeSpan.FromHours(24),
                        createdBy: "AUTHENTICATION_HANDSHAKE",
                        cancellationToken: cancellationToken);
                    sessionToken = authSession.SessionToken;
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<AuthenticateGamerResponseDto>.Success(new AuthenticateGamerResponseDto
                {
                    IsSuccess = true,
                    GamerId = targetGamerId,
                    GamerBusinessId = businessId,
                    Username = username,
                    AccountNumber = account?.AccountNumber,
                    SessionToken = sessionToken
                });
            }
            catch (Exception ex)
            {
                return Result<AuthenticateGamerResponseDto>.Failure("AUTHENTICATION_FAILED", ex.Message);
            }
        }
    }

    public class GetGamerQueryHandler : IQueryHandler<GetGamerQuery, Gamer>
    {
        private readonly IRepository<Gamer> _gamerRepository;

        public GetGamerQueryHandler(IRepository<Gamer> gamerRepository)
        {
            _gamerRepository = gamerRepository ?? throw new ArgumentNullException(nameof(gamerRepository));
        }

        public async Task<Result<Gamer>> HandleAsync(GetGamerQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new GetGamerQueryValidator();
            var validationResult = await validator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<Gamer>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            var gamer = await _gamerRepository.GetByIdAsync(query.GamerEntityId, track: false, cancellationToken);
            if (gamer == null)
            {
                return Result<Gamer>.Failure("NOT_FOUND", $"Gamer with ID '{query.GamerEntityId}' not found.");
            }

            return Result<Gamer>.Success(gamer);
        }
    }

    public class GetGamerAccountQueryHandler : IQueryHandler<GetGamerAccountQuery, GamerAccount>
    {
        private readonly IRepository<GamerAccount> _gamerAccountRepository;

        public GetGamerAccountQueryHandler(IRepository<GamerAccount> gamerAccountRepository)
        {
            _gamerAccountRepository = gamerAccountRepository ?? throw new ArgumentNullException(nameof(gamerAccountRepository));
        }

        public async Task<Result<GamerAccount>> HandleAsync(GetGamerAccountQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new GetGamerAccountQueryValidator();
            var validationResult = await validator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<GamerAccount>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            var account = await _gamerAccountRepository.FirstOrDefaultAsync(a => a.GamerEntityId == query.GamerEntityId, track: false, cancellationToken);
            if (account == null)
            {
                return Result<GamerAccount>.Failure("NOT_FOUND", $"Gamer account for gamer ID '{query.GamerEntityId}' not found.");
            }

            return Result<GamerAccount>.Success(account);
        }
    }
}

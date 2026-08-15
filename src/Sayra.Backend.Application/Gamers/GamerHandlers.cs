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
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IUnitOfWork _unitOfWork;

        public CreateGamerCommandHandler(
            IRepository<Gamer> gamerRepository,
            IRepository<GamerCredential> gamerCredentialRepository,
            IRepository<GamerAccount> gamerAccountRepository,
            IRepository<AuditEvent> auditEventRepository,
            IPasswordHasher passwordHasher,
            IUnitOfWork unitOfWork)
        {
            _gamerRepository = gamerRepository ?? throw new ArgumentNullException(nameof(gamerRepository));
            _gamerCredentialRepository = gamerCredentialRepository ?? throw new ArgumentNullException(nameof(gamerCredentialRepository));
            _gamerAccountRepository = gamerAccountRepository ?? throw new ArgumentNullException(nameof(gamerAccountRepository));
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

                // Hash password and store in separate GamerCredential entity
                var (hash, salt) = _passwordHasher.HashPassword(command.Password);
                var credential = new GamerCredential
                {
                    GamerEntityId = gamer.Id,
                    CredentialType = "Password",
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    HashAlgorithm = "PBKDF2",
                    FailedAttemptCount = 0,
                    IsLocked = false,
                    LastPasswordChangedAt = DateTime.UtcNow
                };

                await _gamerCredentialRepository.AddAsync(credential, cancellationToken);

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
        private readonly IUnitOfWork _unitOfWork;

        public UpdateGamerProfileCommandHandler(
            IRepository<Gamer> gamerRepository,
            IUnitOfWork unitOfWork)
        {
            _gamerRepository = gamerRepository ?? throw new ArgumentNullException(nameof(gamerRepository));
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
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public DeactivateGamerCommandHandler(
            IRepository<Gamer> gamerRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _gamerRepository = gamerRepository ?? throw new ArgumentNullException(nameof(gamerRepository));
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
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IUnitOfWork _unitOfWork;

        public ChangeGamerPasswordCommandHandler(
            IRepository<GamerCredential> gamerCredentialRepository,
            IRepository<AuditEvent> auditEventRepository,
            IPasswordHasher passwordHasher,
            IUnitOfWork unitOfWork)
        {
            _gamerCredentialRepository = gamerCredentialRepository ?? throw new ArgumentNullException(nameof(gamerCredentialRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
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

                var credential = await _gamerCredentialRepository.FirstOrDefaultAsync(c => c.GamerEntityId == command.GamerEntityId, track: true, cancellationToken);
                if (credential == null)
                {
                    return Result<bool>.Failure("NOT_FOUND", $"Gamer credentials for ID '{command.GamerEntityId}' not found.");
                }

                if (!_passwordHasher.VerifyPassword(command.CurrentPassword, credential.PasswordHash, credential.PasswordSalt))
                {
                    return Result<bool>.Failure("INVALID_CREDENTIALS", "Current password verification failed.");
                }

                var (newHash, newSalt) = _passwordHasher.HashPassword(command.NewPassword);
                credential.SetPassword(newHash, newSalt, "PBKDF2");
                _gamerCredentialRepository.Update(credential);

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
        private readonly IRepository<GamerAccount> _gamerAccountRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IUnitOfWork _unitOfWork;

        public AuthenticateGamerCommandHandler(
            IRepository<Gamer> gamerRepository,
            IRepository<GamerCredential> gamerCredentialRepository,
            IRepository<GamerAccount> gamerAccountRepository,
            IPasswordHasher passwordHasher,
            IUnitOfWork unitOfWork)
        {
            _gamerRepository = gamerRepository ?? throw new ArgumentNullException(nameof(gamerRepository));
            _gamerCredentialRepository = gamerCredentialRepository ?? throw new ArgumentNullException(nameof(gamerCredentialRepository));
            _gamerAccountRepository = gamerAccountRepository ?? throw new ArgumentNullException(nameof(gamerAccountRepository));
            _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
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
                var gamer = await _gamerRepository.FirstOrDefaultAsync(g => g.Username.ToLower() == inputLower || g.Email.ToLower() == inputLower, track: false, cancellationToken);
                if (gamer == null)
                {
                    return Result<AuthenticateGamerResponseDto>.Success(new AuthenticateGamerResponseDto
                    {
                        IsSuccess = false,
                        ErrorCode = "INVALID_CREDENTIALS",
                        ErrorMessage = "Invalid username/email or password."
                    });
                }

                if (!gamer.CanOperate())
                {
                    return Result<AuthenticateGamerResponseDto>.Success(new AuthenticateGamerResponseDto
                    {
                        IsSuccess = false,
                        ErrorCode = "ACCOUNT_DISABLED",
                        ErrorMessage = $"Gamer account is currently {gamer.Status}."
                    });
                }

                var credential = await _gamerCredentialRepository.FirstOrDefaultAsync(c => c.GamerEntityId == gamer.Id, track: true, cancellationToken);
                if (credential == null)
                {
                    return Result<AuthenticateGamerResponseDto>.Success(new AuthenticateGamerResponseDto
                    {
                        IsSuccess = false,
                        ErrorCode = "INVALID_CREDENTIALS",
                        ErrorMessage = "Invalid username/email or password."
                    });
                }

                if (credential.IsCurrentlyLockedOut())
                {
                    return Result<AuthenticateGamerResponseDto>.Success(new AuthenticateGamerResponseDto
                    {
                        IsSuccess = false,
                        ErrorCode = "ACCOUNT_LOCKED",
                        ErrorMessage = "Account is temporarily locked due to too many failed login attempts."
                    });
                }

                bool isPasswordValid = _passwordHasher.VerifyPassword(command.Password, credential.PasswordHash, credential.PasswordSalt);
                if (!isPasswordValid)
                {
                    credential.RecordFailedAttempt(maxAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15));
                    _gamerCredentialRepository.Update(credential);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);

                    return Result<AuthenticateGamerResponseDto>.Success(new AuthenticateGamerResponseDto
                    {
                        IsSuccess = false,
                        ErrorCode = "INVALID_CREDENTIALS",
                        ErrorMessage = "Invalid username/email or password."
                    });
                }

                // Successful login -> Reset failed attempt counters
                credential.ResetFailedAttempts();
                _gamerCredentialRepository.Update(credential);

                var account = await _gamerAccountRepository.FirstOrDefaultAsync(a => a.GamerEntityId == gamer.Id, track: false, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<AuthenticateGamerResponseDto>.Success(new AuthenticateGamerResponseDto
                {
                    IsSuccess = true,
                    GamerId = gamer.Id,
                    GamerBusinessId = gamer.GamerId,
                    Username = gamer.Username,
                    AccountNumber = account?.AccountNumber
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

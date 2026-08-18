using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Events;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Financial
{
    public class FinancialAccountService : IFinancialAccountService
    {
        private readonly IRepository<GamerAccount> _accountRepository;
        private readonly IRepository<LedgerEntry> _ledgerRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public FinancialAccountService(
            IRepository<GamerAccount> accountRepository,
            IRepository<LedgerEntry> ledgerRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _ledgerRepository = ledgerRepository ?? throw new ArgumentNullException(nameof(ledgerRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<GamerAccount>> GetAccountByGamerIdAsync(Guid gamerEntityId, CancellationToken cancellationToken = default)
        {
            var account = await _accountRepository.FirstOrDefaultAsync(a => a.GamerEntityId == gamerEntityId, track: false, cancellationToken);
            if (account == null)
            {
                return Result<GamerAccount>.Failure("NOT_FOUND", $"Financial account for gamer ID '{gamerEntityId}' not found.");
            }
            return Result<GamerAccount>.Success(account);
        }

        public async Task<Result<GamerAccount>> GetAccountByIdAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            var account = await _accountRepository.GetByIdAsync(accountId, track: false, cancellationToken);
            if (account == null)
            {
                return Result<GamerAccount>.Failure("NOT_FOUND", $"Financial account ID '{accountId}' not found.");
            }
            return Result<GamerAccount>.Success(account);
        }

        public async Task<Result<Money>> GetBalanceAsync(Guid gamerEntityId, CancellationToken cancellationToken = default)
        {
            var account = await _accountRepository.FirstOrDefaultAsync(a => a.GamerEntityId == gamerEntityId, track: false, cancellationToken);
            if (account == null)
            {
                return Result<Money>.Failure("NOT_FOUND", $"Financial account for gamer ID '{gamerEntityId}' not found.");
            }
            return Result<Money>.Success(new Money(account.Balance, account.Currency));
        }

        public async Task<Result<LedgerEntry>> CreditAccountAsync(
            Guid accountId,
            Money amount,
            string reference,
            string entryType = "CREDIT",
            string? correlationId = null,
            string? actor = null,
            string? description = null,
            CancellationToken cancellationToken = default)
        {
            if (amount == null || amount.Amount <= 0)
            {
                return Result<LedgerEntry>.Failure("INVALID_AMOUNT", "Credit amount must be strictly greater than zero.");
            }

            if (string.IsNullOrWhiteSpace(reference))
            {
                return Result<LedgerEntry>.Failure("INVALID_REFERENCE", "A valid financial operation reference is required.");
            }

            var trimmedRef = reference.Trim();

            try
            {
                return await _unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    // Uniqueness check on reference for completed ledger entries (idempotency foundation)
                    var existingRef = await _ledgerRepository.FirstOrDefaultAsync(e => e.Reference == trimmedRef, track: false, cancellationToken);
                    if (existingRef != null)
                    {
                        return Result<LedgerEntry>.Failure("DUPLICATE_OPERATION", $"Financial operation with reference '{trimmedRef}' has already been processed.");
                    }

                    var account = await _accountRepository.GetByIdAsync(accountId, track: true, cancellationToken);
                    if (account == null)
                    {
                        return Result<LedgerEntry>.Failure("NOT_FOUND", $"Financial account ID '{accountId}' not found.");
                    }

                    decimal oldBalance = account.Balance;
                    var ledgerEntry = account.Credit(amount, trimmedRef, entryType, correlationId, actor, description);

                    _accountRepository.Update(account);
                    await _ledgerRepository.AddAsync(ledgerEntry, cancellationToken);

                    // Audit events
                    var balanceCreditedEvent = new AuditEvent
                    {
                        EventId = Guid.NewGuid(),
                        EventType = nameof(BalanceCredited),
                        EventVersion = 1,
                        Timestamp = DateTime.UtcNow,
                        Payload = JsonSerializer.Serialize(new BalanceCredited(
                            account.Id,
                            amount.Amount,
                            amount.Currency,
                            trimmedRef,
                            account.Balance,
                            DateTime.UtcNow,
                            correlationId ?? string.Empty
                        ))
                    };
                    await _auditEventRepository.AddAsync(balanceCreditedEvent, cancellationToken);

                    var ledgerCreatedEvent = new AuditEvent
                    {
                        EventId = Guid.NewGuid(),
                        EventType = nameof(LedgerEntryCreated),
                        EventVersion = 1,
                        Timestamp = DateTime.UtcNow,
                        Payload = JsonSerializer.Serialize(new LedgerEntryCreated(
                            ledgerEntry.Id,
                            account.Id,
                            ledgerEntry.Amount,
                            ledgerEntry.Currency,
                            ledgerEntry.Direction,
                            ledgerEntry.EntryType,
                            ledgerEntry.Reference,
                            ledgerEntry.BalanceAfter,
                            DateTime.UtcNow,
                            correlationId ?? string.Empty
                        ))
                    };
                    await _auditEventRepository.AddAsync(ledgerCreatedEvent, cancellationToken);

                    var balanceChangedEvent = new AuditEvent
                    {
                        EventId = Guid.NewGuid(),
                        EventType = nameof(BalanceChanged),
                        EventVersion = 1,
                        Timestamp = DateTime.UtcNow,
                        Payload = JsonSerializer.Serialize(new BalanceChanged(
                            account.Id,
                            oldBalance,
                            account.Balance,
                            account.Currency,
                            $"CREDIT:{entryType}",
                            DateTime.UtcNow,
                            correlationId ?? string.Empty
                        ))
                    };
                    await _auditEventRepository.AddAsync(balanceChangedEvent, cancellationToken);

                    return Result<LedgerEntry>.Success(ledgerEntry);
                }, cancellationToken);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<LedgerEntry>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<LedgerEntry>.Failure("CREDIT_FAILED", ex.Message);
            }
        }

        public async Task<Result<LedgerEntry>> DebitAccountAsync(
            Guid accountId,
            Money amount,
            string reference,
            string entryType = "DEBIT",
            string? correlationId = null,
            string? actor = null,
            string? description = null,
            CancellationToken cancellationToken = default)
        {
            if (amount == null || amount.Amount <= 0)
            {
                return Result<LedgerEntry>.Failure("INVALID_AMOUNT", "Debit amount must be strictly greater than zero.");
            }

            if (string.IsNullOrWhiteSpace(reference))
            {
                return Result<LedgerEntry>.Failure("INVALID_REFERENCE", "A valid financial operation reference is required.");
            }

            var trimmedRef = reference.Trim();

            try
            {
                return await _unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    // Check reference uniqueness (idempotency foundation)
                    var existingRef = await _ledgerRepository.FirstOrDefaultAsync(e => e.Reference == trimmedRef, track: false, cancellationToken);
                    if (existingRef != null)
                    {
                        return Result<LedgerEntry>.Failure("DUPLICATE_OPERATION", $"Financial operation with reference '{trimmedRef}' has already been processed.");
                    }

                    var account = await _accountRepository.GetByIdAsync(accountId, track: true, cancellationToken);
                    if (account == null)
                    {
                        return Result<LedgerEntry>.Failure("NOT_FOUND", $"Financial account ID '{accountId}' not found.");
                    }

                    decimal oldBalance = account.Balance;
                    var ledgerEntry = account.Debit(amount, trimmedRef, entryType, correlationId, actor, description);

                    _accountRepository.Update(account);
                    await _ledgerRepository.AddAsync(ledgerEntry, cancellationToken);

                    // Audit events
                    var balanceDebitedEvent = new AuditEvent
                    {
                        EventId = Guid.NewGuid(),
                        EventType = nameof(BalanceDebited),
                        EventVersion = 1,
                        Timestamp = DateTime.UtcNow,
                        Payload = JsonSerializer.Serialize(new BalanceDebited(
                            account.Id,
                            amount.Amount,
                            amount.Currency,
                            trimmedRef,
                            account.Balance,
                            DateTime.UtcNow,
                            correlationId ?? string.Empty
                        ))
                    };
                    await _auditEventRepository.AddAsync(balanceDebitedEvent, cancellationToken);

                    var ledgerCreatedEvent = new AuditEvent
                    {
                        EventId = Guid.NewGuid(),
                        EventType = nameof(LedgerEntryCreated),
                        EventVersion = 1,
                        Timestamp = DateTime.UtcNow,
                        Payload = JsonSerializer.Serialize(new LedgerEntryCreated(
                            ledgerEntry.Id,
                            account.Id,
                            ledgerEntry.Amount,
                            ledgerEntry.Currency,
                            ledgerEntry.Direction,
                            ledgerEntry.EntryType,
                            ledgerEntry.Reference,
                            ledgerEntry.BalanceAfter,
                            DateTime.UtcNow,
                            correlationId ?? string.Empty
                        ))
                    };
                    await _auditEventRepository.AddAsync(ledgerCreatedEvent, cancellationToken);

                    var balanceChangedEvent = new AuditEvent
                    {
                        EventId = Guid.NewGuid(),
                        EventType = nameof(BalanceChanged),
                        EventVersion = 1,
                        Timestamp = DateTime.UtcNow,
                        Payload = JsonSerializer.Serialize(new BalanceChanged(
                            account.Id,
                            oldBalance,
                            account.Balance,
                            account.Currency,
                            $"DEBIT:{entryType}",
                            DateTime.UtcNow,
                            correlationId ?? string.Empty
                        ))
                    };
                    await _auditEventRepository.AddAsync(balanceChangedEvent, cancellationToken);

                    return Result<LedgerEntry>.Success(ledgerEntry);
                }, cancellationToken);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<LedgerEntry>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<LedgerEntry>.Failure("DEBIT_FAILED", ex.Message);
            }
        }

        public async Task<Result<IReadOnlyList<LedgerEntry>>> GetLedgerAsync(
            Guid accountId,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 200) pageSize = 200;

            var entries = await _ledgerRepository.FindAsync(
                e => e.GamerAccountId == accountId,
                track: false,
                cancellationToken: cancellationToken);

            var orderedEntries = entries
                .OrderByDescending(e => e.CreatedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Result<IReadOnlyList<LedgerEntry>>.Success(orderedEntries.AsReadOnly());
        }
    }
}

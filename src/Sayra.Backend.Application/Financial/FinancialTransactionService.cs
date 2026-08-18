// PHASE 03 — STAGE 03-09: Payment / Financial Transaction Engine & Idempotency
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Events;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Financial
{
    public class FinancialTransactionService : IFinancialTransactionService
    {
        private readonly IRepository<FinancialTransaction> _transactionRepository;
        private readonly IRepository<Payment> _paymentRepository;
        private readonly IRepository<GamerAccount> _accountRepository;
        private readonly IRepository<LedgerEntry> _ledgerRepository;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public FinancialTransactionService(
            IRepository<FinancialTransaction> transactionRepository,
            IRepository<Payment> paymentRepository,
            IRepository<GamerAccount> accountRepository,
            IRepository<LedgerEntry> ledgerRepository,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _transactionRepository = transactionRepository ?? throw new ArgumentNullException(nameof(transactionRepository));
            _paymentRepository = paymentRepository ?? throw new ArgumentNullException(nameof(paymentRepository));
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _ledgerRepository = ledgerRepository ?? throw new ArgumentNullException(nameof(ledgerRepository));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<FinancialTransactionResponseDto>> ProcessTransactionAsync(ProcessTransactionRequestDto dto, CancellationToken cancellationToken = default)
        {
            if (dto == null) return Result<FinancialTransactionResponseDto>.Failure("INVALID_REQUEST", "Request body cannot be null.");
            if (dto.GamerAccountId == Guid.Empty) return Result<FinancialTransactionResponseDto>.Failure("INVALID_ACCOUNT_ID", "GamerAccountId is required.");
            if (dto.Amount <= 0) return Result<FinancialTransactionResponseDto>.Failure("INVALID_AMOUNT", "Transaction amount must be strictly greater than zero.");
            if (string.IsNullOrWhiteSpace(dto.IdempotencyKey)) return Result<FinancialTransactionResponseDto>.Failure("INVALID_IDEMPOTENCY_KEY", "IdempotencyKey is required for financial operations.");

            var trimmedKey = dto.IdempotencyKey.Trim();
            var currency = string.IsNullOrWhiteSpace(dto.Currency) ? "SAY" : dto.Currency.Trim().ToUpperInvariant();
            var opType = string.IsNullOrWhiteSpace(dto.OperationType) ? "DEPOSIT" : dto.OperationType.Trim().ToUpperInvariant();
            var refId = (dto.ReferenceId ?? string.Empty).Trim();
            var expectedFingerprint = FinancialTransaction.ComputeFingerprint(dto.GamerAccountId, opType, dto.Amount, currency, refId);

            try
            {
                return await _unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var existing = await _transactionRepository.FirstOrDefaultAsync(t => t.IdempotencyKey == trimmedKey, track: false, cancellationToken);
                    if (existing != null)
                    {
                        return ResolveExistingTransaction(existing, expectedFingerprint);
                    }

                    var tx = new FinancialTransaction
                    {
                        GamerAccountId = dto.GamerAccountId,
                        OperationType = opType,
                        Amount = dto.Amount,
                        Currency = currency,
                        Status = "PENDING",
                        IdempotencyKey = trimmedKey,
                        RequestFingerprint = expectedFingerprint,
                        CorrelationId = (dto.CorrelationId ?? string.Empty).Trim(),
                        ReferenceId = refId,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    tx.NormalizeAndValidate();

                    var account = await _accountRepository.GetByIdAsync(dto.GamerAccountId, track: true, cancellationToken);
                    if (account == null)
                    {
                        tx.Fail($"Financial account '{dto.GamerAccountId}' not found.");
                        await _transactionRepository.AddAsync(tx, cancellationToken);
                        return Result<FinancialTransactionResponseDto>.Failure("NOT_FOUND", $"Financial account ID '{dto.GamerAccountId}' not found.");
                    }

                    if (!account.CanTransact())
                    {
                        tx.Fail($"Account '{account.Id}' is in status '{account.Status}' and cannot process financial operations.");
                        await _transactionRepository.AddAsync(tx, cancellationToken);
                        return Result<FinancialTransactionResponseDto>.Failure("ACCOUNT_DISABLED", $"Account '{account.Id}' is in status '{account.Status}' and cannot process financial operations.");
                    }

                    LedgerEntry ledgerEntry;
                    var isCredit = IsCreditOperation(opType);

                    if (isCredit)
                    {
                        ledgerEntry = account.Credit(
                            new Money(dto.Amount, currency),
                            string.IsNullOrEmpty(refId) ? trimmedKey : refId,
                            opType,
                            dto.CorrelationId,
                            "SYSTEM",
                            dto.Description);
                    }
                    else
                    {
                        ledgerEntry = account.Debit(
                            new Money(dto.Amount, currency),
                            string.IsNullOrEmpty(refId) ? trimmedKey : refId,
                            opType,
                            dto.CorrelationId,
                            "SYSTEM",
                            dto.Description);
                    }

                    _accountRepository.Update(account);
                    await _ledgerRepository.AddAsync(ledgerEntry, cancellationToken);

                    tx.Complete(ledgerEntry.Id);
                    await _transactionRepository.AddAsync(tx, cancellationToken);

                    var createdEvent = new AuditEvent
                    {
                        EventId = Guid.NewGuid(),
                        EventType = nameof(FinancialTransactionCompleted),
                        EventVersion = 1,
                        Timestamp = DateTime.UtcNow,
                        Payload = JsonSerializer.Serialize(new FinancialTransactionCompleted(
                            tx.Id,
                            account.Id,
                            ledgerEntry.Id,
                            tx.Amount,
                            tx.Currency,
                            tx.OperationType,
                            DateTime.UtcNow,
                            tx.CorrelationId
                        ))
                    };
                    await _auditEventRepository.AddAsync(createdEvent, cancellationToken);

                    return Result<FinancialTransactionResponseDto>.Success(MapToDto(tx));
                }, cancellationToken);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<FinancialTransactionResponseDto>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                if (ex.GetType().Name.Contains("DbUpdate") || ex.InnerException?.Message.Contains("IdempotencyKey") == true)
                {
                    var existingRace = await _transactionRepository.FirstOrDefaultAsync(t => t.IdempotencyKey == trimmedKey, track: false, cancellationToken);
                    if (existingRace != null)
                    {
                        return ResolveExistingTransaction(existingRace, expectedFingerprint);
                    }
                }

                return Result<FinancialTransactionResponseDto>.Failure("TRANSACTION_FAILED", ex.Message);
            }
        }

        public async Task<Result<FinancialTransactionResponseDto>> ReverseTransactionAsync(ReverseTransactionRequestDto dto, CancellationToken cancellationToken = default)
        {
            if (dto == null) return Result<FinancialTransactionResponseDto>.Failure("INVALID_REQUEST", "Request body cannot be null.");
            if (dto.OriginalTransactionId == Guid.Empty) return Result<FinancialTransactionResponseDto>.Failure("INVALID_TRANSACTION_ID", "OriginalTransactionId is required.");
            if (string.IsNullOrWhiteSpace(dto.IdempotencyKey)) return Result<FinancialTransactionResponseDto>.Failure("INVALID_IDEMPOTENCY_KEY", "IdempotencyKey is required for reversal operation.");

            var trimmedKey = dto.IdempotencyKey.Trim();

            try
            {
                return await _unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var existingReversal = await _transactionRepository.FirstOrDefaultAsync(t => t.IdempotencyKey == trimmedKey, track: false, cancellationToken);
                    if (existingReversal != null)
                    {
                        if (existingReversal.OriginalTransactionId != dto.OriginalTransactionId)
                        {
                            return Result<FinancialTransactionResponseDto>.Failure("IDEMPOTENCY_CONFLICT", "Idempotency key reuse detected with different original transaction ID.");
                        }

                        return Result<FinancialTransactionResponseDto>.Success(MapToDto(existingReversal));
                    }

                    var originalTx = await _transactionRepository.GetByIdAsync(dto.OriginalTransactionId, track: true, cancellationToken);
                    if (originalTx == null)
                    {
                        return Result<FinancialTransactionResponseDto>.Failure("NOT_FOUND", $"Original financial transaction '{dto.OriginalTransactionId}' not found.");
                    }

                    if (originalTx.Status == "REVERSED")
                    {
                        return Result<FinancialTransactionResponseDto>.Failure("DUPLICATE_REVERSAL", $"Transaction '{dto.OriginalTransactionId}' has already been reversed.");
                    }

                    if (originalTx.Status != "COMPLETED")
                    {
                        return Result<FinancialTransactionResponseDto>.Failure("INVALID_STATE_TRANSITION", $"Cannot reverse transaction '{dto.OriginalTransactionId}' in status '{originalTx.Status}'. Only COMPLETED transactions can be reversed.");
                    }

                    var account = await _accountRepository.GetByIdAsync(originalTx.GamerAccountId, track: true, cancellationToken);
                    if (account == null)
                    {
                        return Result<FinancialTransactionResponseDto>.Failure("NOT_FOUND", $"Financial account '{originalTx.GamerAccountId}' not found.");
                    }

                    var isOriginalCredit = IsCreditOperation(originalTx.OperationType);
                    var reversalOpType = isOriginalCredit ? "REVERSAL" : "REFUND";
                    var expectedFingerprint = FinancialTransaction.ComputeFingerprint(account.Id, reversalOpType, originalTx.Amount, originalTx.Currency, $"REV-{originalTx.Id:N}");

                    var reversalTx = new FinancialTransaction
                    {
                        GamerAccountId = account.Id,
                        OperationType = reversalOpType,
                        Amount = originalTx.Amount,
                        Currency = originalTx.Currency,
                        Status = "PENDING",
                        IdempotencyKey = trimmedKey,
                        RequestFingerprint = expectedFingerprint,
                        CorrelationId = (dto.CorrelationId ?? originalTx.CorrelationId).Trim(),
                        ReferenceId = $"REV-{originalTx.Id:N}",
                        OriginalTransactionId = originalTx.Id,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    reversalTx.NormalizeAndValidate();

                    LedgerEntry ledgerEntry;
                    if (isOriginalCredit)
                    {
                        ledgerEntry = account.Debit(
                            new Money(originalTx.Amount, originalTx.Currency),
                            $"REV-{originalTx.Id:N}",
                            "REVERSAL",
                            dto.CorrelationId,
                            "SYSTEM",
                            $"Reversal of transaction {originalTx.Id}: {dto.Reason}");
                    }
                    else
                    {
                        ledgerEntry = account.Credit(
                            new Money(originalTx.Amount, originalTx.Currency),
                            $"REV-{originalTx.Id:N}",
                            "REFUND",
                            dto.CorrelationId,
                            "SYSTEM",
                            $"Refund for transaction {originalTx.Id}: {dto.Reason}");
                    }

                    _accountRepository.Update(account);
                    await _ledgerRepository.AddAsync(ledgerEntry, cancellationToken);

                    reversalTx.Complete(ledgerEntry.Id);
                    originalTx.Reverse(reversalTx.Id);

                    _transactionRepository.Update(originalTx);
                    await _transactionRepository.AddAsync(reversalTx, cancellationToken);

                    var reversedEvent = new AuditEvent
                    {
                        EventId = Guid.NewGuid(),
                        EventType = nameof(FinancialTransactionReversed),
                        EventVersion = 1,
                        Timestamp = DateTime.UtcNow,
                        Payload = JsonSerializer.Serialize(new FinancialTransactionReversed(
                            originalTx.Id,
                            reversalTx.Id,
                            account.Id,
                            originalTx.Amount,
                            originalTx.Currency,
                            DateTime.UtcNow,
                            reversalTx.CorrelationId
                        ))
                    };
                    await _auditEventRepository.AddAsync(reversedEvent, cancellationToken);

                    return Result<FinancialTransactionResponseDto>.Success(MapToDto(reversalTx));
                }, cancellationToken);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<FinancialTransactionResponseDto>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<FinancialTransactionResponseDto>.Failure("REVERSAL_FAILED", ex.Message);
            }
        }

        public async Task<Result<PaymentResponseDto>> CreatePaymentAsync(CreatePaymentRequestDto dto, CancellationToken cancellationToken = default)
        {
            if (dto == null) return Result<PaymentResponseDto>.Failure("INVALID_REQUEST", "Request body cannot be null.");
            if (dto.GamerAccountId == Guid.Empty) return Result<PaymentResponseDto>.Failure("INVALID_ACCOUNT_ID", "GamerAccountId is required.");
            if (dto.Amount <= 0) return Result<PaymentResponseDto>.Failure("INVALID_AMOUNT", "Payment amount must be strictly greater than zero.");
            if (string.IsNullOrWhiteSpace(dto.IdempotencyKey)) return Result<PaymentResponseDto>.Failure("INVALID_IDEMPOTENCY_KEY", "IdempotencyKey is required for payment.");

            var trimmedKey = dto.IdempotencyKey.Trim();
            var currency = string.IsNullOrWhiteSpace(dto.Currency) ? "SAY" : dto.Currency.Trim().ToUpperInvariant();

            try
            {
                return await _unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var existingPayment = await _paymentRepository.FirstOrDefaultAsync(p => p.IdempotencyKey == trimmedKey, track: false, cancellationToken);
                    if (existingPayment != null)
                    {
                        if (existingPayment.GamerAccountId != dto.GamerAccountId ||
                            existingPayment.Amount != dto.Amount ||
                            !string.Equals(existingPayment.Currency, currency, StringComparison.OrdinalIgnoreCase))
                        {
                            return Result<PaymentResponseDto>.Failure("IDEMPOTENCY_CONFLICT", "Idempotency key reuse detected with different payment parameters.");
                        }

                        return Result<PaymentResponseDto>.Success(MapToPaymentDto(existingPayment));
                    }

                    var payment = new Payment
                    {
                        GamerAccountId = dto.GamerAccountId,
                        Amount = dto.Amount,
                        Currency = currency,
                        Status = "PENDING",
                        PaymentMethod = string.IsNullOrWhiteSpace(dto.PaymentMethod) ? "ACCOUNT_BALANCE" : dto.PaymentMethod.Trim().ToUpperInvariant(),
                        IdempotencyKey = trimmedKey,
                        Reference = string.IsNullOrWhiteSpace(dto.Reference) ? trimmedKey : dto.Reference.Trim(),
                        Description = dto.Description,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    payment.NormalizeAndValidate();

                    var processDto = new ProcessTransactionRequestDto
                    {
                        GamerAccountId = dto.GamerAccountId,
                        OperationType = "PAYMENT",
                        Amount = dto.Amount,
                        Currency = payment.Currency,
                        IdempotencyKey = $"TX-{trimmedKey}",
                        ReferenceId = payment.Reference,
                        Description = dto.Description,
                        CorrelationId = dto.CorrelationId
                    };

                    var txResult = await ProcessTransactionAsync(processDto, cancellationToken);
                    if (!txResult.IsSuccess)
                    {
                        payment.MarkFailed(txResult.ErrorMessage ?? "Payment processing failed.");
                        await _paymentRepository.AddAsync(payment, cancellationToken);
                        return Result<PaymentResponseDto>.Failure(txResult.ErrorCode ?? "PAYMENT_FAILED", txResult.ErrorMessage ?? "Payment processing failed.");
                    }

                    payment.MarkCompleted(txResult.Value!.Id);
                    await _paymentRepository.AddAsync(payment, cancellationToken);

                    return Result<PaymentResponseDto>.Success(MapToPaymentDto(payment));
                }, cancellationToken);
            }
            catch (InvalidDomainException domainEx)
            {
                return Result<PaymentResponseDto>.Failure(domainEx.ErrorCode, domainEx.Message);
            }
            catch (Exception ex)
            {
                return Result<PaymentResponseDto>.Failure("PAYMENT_FAILED", ex.Message);
            }
        }

        public async Task<Result<FinancialTransactionResponseDto>> GetTransactionByIdAsync(Guid transactionId, CancellationToken cancellationToken = default)
        {
            var tx = await _transactionRepository.GetByIdAsync(transactionId, track: false, cancellationToken);
            if (tx == null)
            {
                return Result<FinancialTransactionResponseDto>.Failure("NOT_FOUND", $"Financial transaction '{transactionId}' not found.");
            }
            return Result<FinancialTransactionResponseDto>.Success(MapToDto(tx));
        }

        public async Task<Result<FinancialTransactionResponseDto>> GetTransactionByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return Result<FinancialTransactionResponseDto>.Failure("INVALID_IDEMPOTENCY_KEY", "Idempotency key cannot be empty.");
            }

            var trimmedKey = idempotencyKey.Trim();
            var tx = await _transactionRepository.FirstOrDefaultAsync(t => t.IdempotencyKey == trimmedKey, track: false, cancellationToken);
            if (tx == null)
            {
                return Result<FinancialTransactionResponseDto>.Failure("NOT_FOUND", $"Financial transaction with idempotency key '{trimmedKey}' not found.");
            }
            return Result<FinancialTransactionResponseDto>.Success(MapToDto(tx));
        }

        public async Task<Result<PaymentResponseDto>> GetPaymentByIdAsync(Guid paymentId, CancellationToken cancellationToken = default)
        {
            var payment = await _paymentRepository.GetByIdAsync(paymentId, track: false, cancellationToken);
            if (payment == null)
            {
                return Result<PaymentResponseDto>.Failure("NOT_FOUND", $"Payment '{paymentId}' not found.");
            }
            return Result<PaymentResponseDto>.Success(MapToPaymentDto(payment));
        }

        private static Result<FinancialTransactionResponseDto> ResolveExistingTransaction(FinancialTransaction existing, string expectedFingerprint)
        {
            if (string.Equals(existing.RequestFingerprint, expectedFingerprint, StringComparison.Ordinal))
            {
                return Result<FinancialTransactionResponseDto>.Success(MapToDto(existing));
            }

            return Result<FinancialTransactionResponseDto>.Failure("IDEMPOTENCY_CONFLICT", "Idempotency key reuse detected with different request parameters.");
        }

        private static bool IsCreditOperation(string operationType)
        {
            var op = (operationType ?? string.Empty).Trim().ToUpperInvariant();
            return op == "DEPOSIT" || op == "REFUND" || op == "ADJUSTMENT_CREDIT" || op == "RESERVATION_RELEASE" || op == "CREDIT";
        }

        private static FinancialTransactionResponseDto MapToDto(FinancialTransaction tx)
        {
            return new FinancialTransactionResponseDto
            {
                Id = tx.Id,
                GamerAccountId = tx.GamerAccountId,
                OperationType = tx.OperationType,
                Amount = tx.Amount,
                Currency = tx.Currency,
                Status = tx.Status,
                IdempotencyKey = tx.IdempotencyKey,
                RequestFingerprint = tx.RequestFingerprint,
                CorrelationId = tx.CorrelationId,
                ReferenceId = tx.ReferenceId,
                OriginalTransactionId = tx.OriginalTransactionId,
                LedgerEntryId = tx.LedgerEntryId,
                FailureReason = tx.FailureReason,
                CreatedAtUtc = tx.CreatedAtUtc,
                CompletedAtUtc = tx.CompletedAtUtc,
                ReversedAtUtc = tx.ReversedAtUtc
            };
        }

        private static PaymentResponseDto MapToPaymentDto(Payment payment)
        {
            return new PaymentResponseDto
            {
                Id = payment.Id,
                GamerAccountId = payment.GamerAccountId,
                FinancialTransactionId = payment.FinancialTransactionId,
                Amount = payment.Amount,
                Currency = payment.Currency,
                Status = payment.Status,
                PaymentMethod = payment.PaymentMethod,
                IdempotencyKey = payment.IdempotencyKey,
                Reference = payment.Reference,
                Description = payment.Description,
                FailureReason = payment.FailureReason,
                CreatedAtUtc = payment.CreatedAtUtc,
                CompletedAtUtc = payment.CompletedAtUtc
            };
        }
    }
}

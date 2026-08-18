// PHASE 03 — STAGE 03-09: Payment / Financial Transaction Engine & Idempotency
using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Contracts;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Financial
{
    public interface IFinancialTransactionService
    {
        Task<Result<FinancialTransactionResponseDto>> ProcessTransactionAsync(ProcessTransactionRequestDto dto, CancellationToken cancellationToken = default);
        Task<Result<FinancialTransactionResponseDto>> ReverseTransactionAsync(ReverseTransactionRequestDto dto, CancellationToken cancellationToken = default);
        Task<Result<PaymentResponseDto>> CreatePaymentAsync(CreatePaymentRequestDto dto, CancellationToken cancellationToken = default);
        Task<Result<FinancialTransactionResponseDto>> GetTransactionByIdAsync(Guid transactionId, CancellationToken cancellationToken = default);
        Task<Result<FinancialTransactionResponseDto>> GetTransactionByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
        Task<Result<PaymentResponseDto>> GetPaymentByIdAsync(Guid paymentId, CancellationToken cancellationToken = default);
    }
}

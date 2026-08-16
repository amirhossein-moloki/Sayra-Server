using System;
using System.Collections.Generic;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Application.Billing
{
    public record CalculateSessionBillingCommand(
        Guid SessionId,
        decimal? DiscountAmount = null,
        decimal? AdjustmentAmount = null,
        string? CorrelationId = null) : ICommand<BillingResultResponseDto>;

    public record GetBillingResultQuery(Guid BillingResultId) : IQuery<BillingResultResponseDto>;

    public record GetSessionBillingHistoryQuery(Guid SessionId) : IQuery<List<BillingResultResponseDto>>;

    public record GetLatestSessionBillingQuery(Guid SessionId) : IQuery<BillingResultResponseDto>;
}

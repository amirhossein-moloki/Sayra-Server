using System;

namespace Sayra.Backend.Application.OfflineQueue
{
    public class BusinessReconciliationResult
    {
        public string Status { get; set; } = string.Empty;
        public string ReasonCode { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public bool IsSuccess => Status == Sayra.Backend.Contracts.OfflineReconciliationStatus.Accepted || Status == Sayra.Backend.Contracts.OfflineReconciliationStatus.Duplicate;
        public Guid? EntityId { get; set; }
        public object? Details { get; set; }

        public static BusinessReconciliationResult Success(string reasonCode, Guid? entityId = null, object? details = null)
        {
            return new BusinessReconciliationResult
            {
                Status = Sayra.Backend.Contracts.OfflineReconciliationStatus.Accepted,
                ReasonCode = reasonCode,
                EntityId = entityId,
                Details = details
            };
        }

        public static BusinessReconciliationResult Duplicate(string reasonCode, string? message = null, Guid? entityId = null)
        {
            return new BusinessReconciliationResult
            {
                Status = Sayra.Backend.Contracts.OfflineReconciliationStatus.Duplicate,
                ReasonCode = reasonCode,
                ErrorMessage = message,
                EntityId = entityId
            };
        }

        public static BusinessReconciliationResult Conflict(string reasonCode, string message)
        {
            return new BusinessReconciliationResult
            {
                Status = Sayra.Backend.Contracts.OfflineReconciliationStatus.Conflict,
                ReasonCode = reasonCode,
                ErrorMessage = message
            };
        }

        public static BusinessReconciliationResult Reject(string reasonCode, string message)
        {
            return new BusinessReconciliationResult
            {
                Status = Sayra.Backend.Contracts.OfflineReconciliationStatus.Rejected,
                ReasonCode = reasonCode,
                ErrorMessage = message
            };
        }
    }
}

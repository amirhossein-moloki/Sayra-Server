using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public static class UpdateReleaseStatusValidator
    {
        public static bool IsValidTransition(UpdateReleaseStatus currentState, UpdateReleaseStatus newState)
        {
            if (currentState == newState)
            {
                return true; // Idempotent same-state check
            }

            return currentState switch
            {
                UpdateReleaseStatus.Draft => newState is UpdateReleaseStatus.Validated
                                                      or UpdateReleaseStatus.Cancelled,

                UpdateReleaseStatus.Validated => newState is UpdateReleaseStatus.Ready
                                                          or UpdateReleaseStatus.Draft
                                                          or UpdateReleaseStatus.Cancelled,

                UpdateReleaseStatus.Ready => newState is UpdateReleaseStatus.Published
                                                       or UpdateReleaseStatus.Cancelled,

                UpdateReleaseStatus.Published => newState is UpdateReleaseStatus.Active
                                                           or UpdateReleaseStatus.Revoked,

                UpdateReleaseStatus.Active => newState is UpdateReleaseStatus.Superseded
                                                       or UpdateReleaseStatus.Revoked,

                UpdateReleaseStatus.Superseded => newState is UpdateReleaseStatus.Revoked,

                UpdateReleaseStatus.Revoked => false,   // Terminal state
                UpdateReleaseStatus.Cancelled => false, // Terminal state

                _ => false
            };
        }

        public static void ValidateTransition(UpdateReleaseStatus currentState, UpdateReleaseStatus newState)
        {
            if (!IsValidTransition(currentState, newState))
            {
                throw new InvalidDomainException(
                    "INVALID_RELEASE_TRANSITION",
                    $"Cannot transition update release status from '{currentState}' to '{newState}'. The requested transition is illegal under system policy.");
            }
        }
    }
}

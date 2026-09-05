using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public static class UpdatePackageLifecycleValidator
    {
        public static bool IsValidTransition(UpdatePackageLifecycleState currentState, UpdatePackageLifecycleState newState)
        {
            if (currentState == newState)
            {
                return true; // Idempotent same-state check
            }

            return currentState switch
            {
                UpdatePackageLifecycleState.Uploading => newState is UpdatePackageLifecycleState.Uploaded
                                                                 or UpdatePackageLifecycleState.ValidationFailed
                                                                 or UpdatePackageLifecycleState.Quarantined,

                UpdatePackageLifecycleState.Uploaded => newState is UpdatePackageLifecycleState.Validating
                                                                or UpdatePackageLifecycleState.ValidationFailed
                                                                or UpdatePackageLifecycleState.Quarantined,

                UpdatePackageLifecycleState.Validating => newState is UpdatePackageLifecycleState.Validated
                                                                  or UpdatePackageLifecycleState.ValidationFailed
                                                                  or UpdatePackageLifecycleState.Quarantined,

                UpdatePackageLifecycleState.Validated => newState is UpdatePackageLifecycleState.Signed
                                                                 or UpdatePackageLifecycleState.ValidationFailed
                                                                 or UpdatePackageLifecycleState.Quarantined,

                UpdatePackageLifecycleState.Signed => newState is UpdatePackageLifecycleState.Ready
                                                              or UpdatePackageLifecycleState.ValidationFailed
                                                              or UpdatePackageLifecycleState.Quarantined,

                UpdatePackageLifecycleState.Ready => newState is UpdatePackageLifecycleState.Quarantined,

                UpdatePackageLifecycleState.ValidationFailed => newState is UpdatePackageLifecycleState.Uploading
                                                                          or UpdatePackageLifecycleState.Quarantined,

                UpdatePackageLifecycleState.Quarantined => false, // Terminal quarantine state

                _ => false
            };
        }

        public static void ValidateTransition(UpdatePackageLifecycleState currentState, UpdatePackageLifecycleState newState)
        {
            if (!IsValidTransition(currentState, newState))
            {
                throw new InvalidDomainException(
                    "INVALID_PACKAGE_LIFECYCLE_TRANSITION",
                    $"Cannot transition update package lifecycle state from '{currentState}' to '{newState}'. The requested transition is illegal under system policy.");
            }
        }
    }
}

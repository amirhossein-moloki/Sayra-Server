using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public static class ConfigurationLifecycleValidator
    {
        public static bool IsValidTransition(ConfigurationLifecycleState currentState, ConfigurationLifecycleState newState)
        {
            if (currentState == newState)
            {
                return true; // Idempotent same-state check
            }

            return currentState switch
            {
                ConfigurationLifecycleState.Draft => newState is ConfigurationLifecycleState.Validated
                                                              or ConfigurationLifecycleState.Revoked,

                ConfigurationLifecycleState.Validated => newState is ConfigurationLifecycleState.Signed
                                                                  or ConfigurationLifecycleState.Revoked,

                ConfigurationLifecycleState.Signed => newState is ConfigurationLifecycleState.Validated
                                                               or ConfigurationLifecycleState.Published
                                                               or ConfigurationLifecycleState.Revoked,

                ConfigurationLifecycleState.Published => newState is ConfigurationLifecycleState.Active
                                                                 or ConfigurationLifecycleState.Revoked,

                ConfigurationLifecycleState.Active => newState is ConfigurationLifecycleState.Superseded
                                                               or ConfigurationLifecycleState.Revoked,

                ConfigurationLifecycleState.Superseded => newState is ConfigurationLifecycleState.Revoked,

                ConfigurationLifecycleState.Revoked => false, // Terminal state

                _ => false
            };
        }

        public static void ValidateTransition(ConfigurationLifecycleState currentState, ConfigurationLifecycleState newState)
        {
            if (!IsValidTransition(currentState, newState))
            {
                throw new InvalidDomainException(
                    "INVALID_LIFECYCLE_TRANSITION",
                    $"Cannot transition configuration publication state from '{currentState}' to '{newState}'. The requested transition is illegal under system policy.");
            }
        }
    }
}

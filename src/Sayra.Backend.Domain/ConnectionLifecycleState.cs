using System;

namespace Sayra.Backend.Domain
{
    public enum ConnectionLifecycleState
    {
        Connecting,
        Authenticating,
        Authenticated,
        Active,
        Disconnected,
        Degraded,
        Terminated
    }

    public static class ConnectionLifecycleValidator
    {
        public static bool IsValidTransition(ConnectionLifecycleState currentState, ConnectionLifecycleState newState)
        {
            if (currentState == newState) return true;

            return currentState switch
            {
                ConnectionLifecycleState.Connecting => newState is ConnectionLifecycleState.Authenticating
                                                                or ConnectionLifecycleState.Authenticated
                                                                or ConnectionLifecycleState.Active
                                                                or ConnectionLifecycleState.Disconnected
                                                                or ConnectionLifecycleState.Terminated,
                ConnectionLifecycleState.Authenticating => newState is ConnectionLifecycleState.Authenticated
                                                                     or ConnectionLifecycleState.Active
                                                                     or ConnectionLifecycleState.Disconnected
                                                                     or ConnectionLifecycleState.Terminated,
                ConnectionLifecycleState.Authenticated => newState is ConnectionLifecycleState.Active
                                                                   or ConnectionLifecycleState.Degraded
                                                                   or ConnectionLifecycleState.Disconnected
                                                                   or ConnectionLifecycleState.Terminated,
                ConnectionLifecycleState.Active => newState is ConnectionLifecycleState.Degraded
                                                           or ConnectionLifecycleState.Disconnected
                                                           or ConnectionLifecycleState.Terminated,
                ConnectionLifecycleState.Degraded => newState is ConnectionLifecycleState.Active
                                                             or ConnectionLifecycleState.Authenticated
                                                             or ConnectionLifecycleState.Disconnected
                                                             or ConnectionLifecycleState.Terminated,
                ConnectionLifecycleState.Disconnected => newState is ConnectionLifecycleState.Terminated,
                ConnectionLifecycleState.Terminated => false,
                _ => false
            };
        }

        public static void ValidateTransition(ConnectionLifecycleState currentState, ConnectionLifecycleState newState)
        {
            if (!IsValidTransition(currentState, newState))
            {
                throw new InvalidOperationException($"Invalid connection lifecycle state transition from {currentState} to {newState}.");
            }
        }
    }
}

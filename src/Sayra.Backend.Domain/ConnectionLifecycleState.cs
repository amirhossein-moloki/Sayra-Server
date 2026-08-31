using System;

namespace Sayra.Backend.Domain
{
    public enum ConnectionLifecycleState
    {
        Connecting,
        Authenticating,
        Authenticated,
        Active,
        Disconnected
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
                                                                or ConnectionLifecycleState.Disconnected,
                ConnectionLifecycleState.Authenticating => newState is ConnectionLifecycleState.Authenticated
                                                                     or ConnectionLifecycleState.Active
                                                                     or ConnectionLifecycleState.Disconnected,
                ConnectionLifecycleState.Authenticated => newState is ConnectionLifecycleState.Active
                                                                   or ConnectionLifecycleState.Disconnected,
                ConnectionLifecycleState.Active => newState is ConnectionLifecycleState.Disconnected,
                ConnectionLifecycleState.Disconnected => false,
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

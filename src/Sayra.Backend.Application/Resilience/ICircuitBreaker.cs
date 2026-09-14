using System;

#nullable enable

namespace Sayra.Backend.Application.Resilience
{
    public interface ICircuitBreaker
    {
        string DependencyName { get; }
        CircuitState State { get; }
        bool AllowExecution();
        void RecordSuccess();
        void RecordFailure(Exception? ex = null);
        void Reset();
    }
}

using System.Collections.Generic;

namespace Sayra.Backend.Application.Abstractions.Transport
{
    public interface ITcpConnectionRegistry
    {
        void Register(ITcpConnection connection);
        void Unregister(string connectionId);
        ITcpConnection? Get(string connectionId);
        ITcpConnection? GetByPcId(string pcId);
        IEnumerable<ITcpConnection> GetAll();
        int Count { get; }
    }
}

using System.Collections.Concurrent;
using System.Collections.Generic;
using Sayra.Backend.Application.Abstractions.Transport;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class TcpConnectionRegistry : ITcpConnectionRegistry
    {
        private readonly ConcurrentDictionary<string, ITcpConnection> _connections = new();

        public void Register(ITcpConnection connection)
        {
            _connections[connection.ConnectionId] = connection;
        }

        public void Unregister(string connectionId)
        {
            _connections.TryRemove(connectionId, out _);
        }

        public ITcpConnection? Get(string connectionId)
        {
            _connections.TryGetValue(connectionId, out var connection);
            return connection;
        }

        public IEnumerable<ITcpConnection> GetAll()
        {
            return _connections.Values;
        }

        public int Count => _connections.Count;
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Sayra.Backend.Application.Abstractions.Transport;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class TcpConnectionRegistry : ITcpConnectionRegistry
    {
        private readonly ConcurrentDictionary<string, ITcpConnection> _connections = new();

        public void Register(ITcpConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            _connections[connection.ConnectionId] = connection;
        }

        public void Unregister(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId)) return;
            _connections.TryRemove(connectionId, out _);
        }

        public ITcpConnection? Get(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId)) return null;
            _connections.TryGetValue(connectionId, out var connection);
            return connection;
        }

        public ITcpConnection? GetByPcId(string pcId)
        {
            if (string.IsNullOrWhiteSpace(pcId)) return null;
            string normalized = pcId.Trim().ToUpperInvariant();
            return _connections.Values.FirstOrDefault(c => string.Equals(c.PcId?.Trim(), normalized, StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<ITcpConnection> GetAll()
        {
            return _connections.Values;
        }

        public int Count => _connections.Count;
    }
}

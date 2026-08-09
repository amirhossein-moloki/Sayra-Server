using System;
using System.Threading;

namespace Sayra.Backend.Shared
{
    public static class CorrelationContext
    {
        private static readonly AsyncLocal<string> _correlationId = new AsyncLocal<string>();

        public static string CorrelationId
        {
            get => _correlationId.Value ?? string.Empty;
            set => _correlationId.Value = value;
        }

        public static void SetCorrelationId(string correlationId)
        {
            _correlationId.Value = correlationId ?? Guid.NewGuid().ToString();
        }

        public static string GetOrCreate()
        {
            if (string.IsNullOrEmpty(_correlationId.Value))
            {
                _correlationId.Value = Guid.NewGuid().ToString();
            }
            return _correlationId.Value;
        }
    }
}

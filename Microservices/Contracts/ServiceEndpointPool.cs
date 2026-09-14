using System;
using System.Collections.Generic;
using System.Linq;

namespace Inventory.Contracts
{
    public sealed class ServiceEndpointPool
    {
        private const int FailuresBeforeUnhealthy = 2;
        private const long ReAdmitAfterMs = 30_000;

        private readonly string[] _endpoints;
        private readonly int[] _consecutiveFailures;
        private readonly long[] _unhealthyUntil;
        private readonly object _gate = new();
        private int _cursor;

        public ServiceEndpointPool(string baseUrlList)
        {
            _endpoints = (baseUrlList ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(e => e.TrimEnd('/'))
                .ToArray();
            if (_endpoints.Length == 0)
                throw new ArgumentException(
                    "At least one endpoint is required (comma-separated BaseUrl list).", nameof(baseUrlList));
            _consecutiveFailures = new int[_endpoints.Length];
            _unhealthyUntil = new long[_endpoints.Length];
        }

        public IReadOnlyList<string> Endpoints => _endpoints;

        public string Next()
        {
            lock (_gate)
            {
                var now = Environment.TickCount64;
                for (var i = 0; i < _endpoints.Length; i++)
                {
                    if (_unhealthyUntil[i] != 0 && now >= _unhealthyUntil[i])
                        _unhealthyUntil[i] = 0;
                }
                for (var i = 0; i < _endpoints.Length; i++)
                {
                    var index = _cursor;
                    _cursor = (_cursor + 1) % _endpoints.Length;
                    if (_unhealthyUntil[index] == 0)
                        return _endpoints[index];
                }
                var fallback = _cursor;
                _cursor = (_cursor + 1) % _endpoints.Length;
                return _endpoints[fallback];
            }
        }

        public void ReportSuccess(string endpoint)
        {
            lock (_gate)
            {
                var index = Array.IndexOf(_endpoints, endpoint);
                if (index < 0) return;
                _consecutiveFailures[index] = 0;
                _unhealthyUntil[index] = 0;
            }
        }

        public void ReportFailure(string endpoint)
        {
            lock (_gate)
            {
                var index = Array.IndexOf(_endpoints, endpoint);
                if (index < 0) return;
                if (++_consecutiveFailures[index] >= FailuresBeforeUnhealthy)
                {
                    _consecutiveFailures[index] = 0;
                    _unhealthyUntil[index] = Environment.TickCount64 + ReAdmitAfterMs;
                }
            }
        }
    }
}

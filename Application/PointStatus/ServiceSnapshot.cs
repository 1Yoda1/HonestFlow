using System;

namespace HonestFlow.Application.PointStatus
{
    public sealed class ServiceSnapshot
    {
        public ServiceSnapshot(string serviceName, string state)
        {
            ServiceName = serviceName;
            State = state;
        }

        public string ServiceName { get; }
        public string State { get; }
        public bool IsRunning => State.IndexOf("Running", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

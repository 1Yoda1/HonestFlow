using System;
using System.Collections.Generic;
using System.Linq;

namespace HonestFlow.Application.PointStatus
{
    public sealed class NodeStatus
    {
        public NodeStatus(NodeLevel level, string shortText, string details, IReadOnlyList<ServiceSnapshot> services = null, string statusText = null)
        {
            Level = level;
            ShortText = shortText;
            Details = details;
            Services = services ?? Array.Empty<ServiceSnapshot>();
            StatusText = statusText;
        }

        public NodeLevel Level { get; }
        public string ShortText { get; }
        public string Details { get; }
        public string StatusText { get; }
        public IReadOnlyList<ServiceSnapshot> Services { get; }
        public bool CanManageServices => Services.Count > 0;
        public string ActionText => !CanManageServices
            ? "Подробнее"
            : Services.Any(x => !x.IsRunning) ? "Запустить" : "Перезапуск";
    }
}

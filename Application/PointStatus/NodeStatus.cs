using System;
using System.Collections.Generic;
using System.Linq;

namespace HonestFlow.Application.PointStatus
{
    public enum NodeActionKind
    {
        Default,
        ManageServices,
        InstallRuDesktop,
        ReinstallRuDesktop,
        RequestRuDesktopHelp
    }

    public sealed class NodeStatus
    {
        public NodeStatus(
            NodeLevel level,
            string shortText,
            string details,
            IReadOnlyList<ServiceSnapshot> services = null,
            string statusText = null,
            NodeActionKind actionKind = NodeActionKind.Default)
        {
            Level = level;
            ShortText = shortText;
            Details = details;
            Services = services ?? Array.Empty<ServiceSnapshot>();
            StatusText = statusText;
            ActionKind = actionKind;
        }

        public NodeLevel Level { get; }
        public string ShortText { get; }
        public string Details { get; }
        public string StatusText { get; }
        public IReadOnlyList<ServiceSnapshot> Services { get; }
        public NodeActionKind ActionKind { get; }
        public bool CanManageServices => Services.Count > 0;

        public string ActionText => ActionKind switch
        {
            NodeActionKind.InstallRuDesktop => "Установить",
            NodeActionKind.ReinstallRuDesktop => "Переустановить",
            NodeActionKind.RequestRuDesktopHelp => "Запросить помощь",
            _ => !CanManageServices
                ? "Подробнее"
                : Services.Any(x => !x.IsRunning) ? "Запустить" : "Перезапуск"
        };
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace HonestFlow.Application.PointStatus
{
    public enum TopologyVisualState
    {
        Healthy,
        Uncertain,
        Missing,
        Ignored
    }

    public sealed class TopologyLinkPresentation
    {
        public TopologyLinkPresentation(TopologyVisualState state, string explanation)
        {
            State = state;
            Explanation = explanation ?? string.Empty;
        }
        public TopologyVisualState State { get; }
        public string Explanation { get; }
    }

    public sealed class TopologyPresentation
    {
        public TopologyVisualState LmFrame { get; init; }
        public TopologyVisualState EsmFrame { get; init; }
        public TopologyVisualState KktFrame { get; init; }
        public TopologyVisualState ControllerFrame { get; init; }
        public TopologyLinkPresentation CloudToEsm { get; init; }
        public TopologyLinkPresentation EsmToController { get; init; }
        public TopologyLinkPresentation ControllerToLm { get; init; }
        public TopologyLinkPresentation EsmToKkt { get; init; }
    }

    public sealed class TopologyPresentationService
    {
        public TopologyPresentation Create(PointStatusResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            return new TopologyPresentation
            {
                LmFrame = FrameFromServices(result.Lm),
                EsmFrame = FrameFromServices(result.Esm),
                KktFrame = FrameFromServices(result.Kkt),
                ControllerFrame = FrameFromServices(result.Controller),
                CloudToEsm = LinkFromCloud(result.Cloud),
                EsmToController = LinkBetween(result.Esm, result.Controller, "ЕСМ", "контроллером"),
                ControllerToLm = LinkBetween(result.Controller, result.Lm, "контроллер", "ЛМ ЧЗ"),
                EsmToKkt = LinkBetween(result.Esm, result.Kkt, "ЕСМ", "ККТ")
            };
        }

        public TopologyPresentation Create(DiagnosticsSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            return new TopologyPresentation
            {
                LmFrame = Frame(snapshot.Lm),
                EsmFrame = Frame(snapshot.Esm),
                KktFrame = Frame(snapshot.Kkt),
                ControllerFrame = Frame(snapshot.Controller),
                CloudToEsm = Link(snapshot.GismtToEsm),
                EsmToController = Link(snapshot.EsmToController),
                ControllerToLm = Link(snapshot.LmConnection),
                EsmToKkt = Link(snapshot.EsmToKkt)
            };
        }

        private static TopologyVisualState Frame(DiagnosticComponentFact fact) => fact?.State switch
        {
            DiagnosticState.Healthy => TopologyVisualState.Healthy,
            DiagnosticState.Failed => TopologyVisualState.Missing,
            _ => TopologyVisualState.Uncertain
        };

        private static TopologyLinkPresentation Link(DiagnosticConnectionFact fact) => new(
            fact?.State switch
            {
                DiagnosticConnectionState.Connected => TopologyVisualState.Healthy,
                DiagnosticConnectionState.Disconnected => TopologyVisualState.Missing,
                _ => TopologyVisualState.Uncertain
            }, fact?.Details);

        private static TopologyVisualState FrameFromServices(NodeStatus node) =>
            node?.Services != null && node.Services.Count > 0 && node.Services.All(service => service.IsRunning)
                ? TopologyVisualState.Healthy
                : TopologyVisualState.Missing;

        private static TopologyLinkPresentation LinkFromCloud(NodeStatus cloud)
        {
            if (cloud?.Level == NodeLevel.Ok)
                return new TopologyLinkPresentation(TopologyVisualState.Healthy, "Связь с облаком установлена.");
            return new TopologyLinkPresentation(TopologyVisualState.Uncertain, Explain(cloud, "Не удалось подтвердить связь с облаком."));
        }

        private static TopologyLinkPresentation LinkBetween(NodeStatus source, NodeStatus target, string sourceName, string targetName)
        {
            if (HasMissingComponent(source) || HasMissingComponent(target))
                return new TopologyLinkPresentation(TopologyVisualState.Missing, $"Связь между {sourceName} и {targetName} невозможна: отсутствует необходимый компонент или набор служб.");
            if (HasStoppedServices(source) || HasStoppedServices(target))
                return new TopologyLinkPresentation(TopologyVisualState.Uncertain, $"Связь между {sourceName} и {targetName} не проверена полностью: одна или несколько служб остановлены.");
            if (source?.Level == NodeLevel.Ok && target?.Level == NodeLevel.Ok)
                return new TopologyLinkPresentation(TopologyVisualState.Healthy, $"Связь между {sourceName} и {targetName} подтверждена.");
            return new TopologyLinkPresentation(TopologyVisualState.Uncertain, Explain(target, $"При проверке связи между {sourceName} и {targetName} обнаружена проблема."));
        }

        private static bool HasMissingComponent(NodeStatus node) => node == null || node.Services == null || node.Services.Count == 0;
        private static bool HasStoppedServices(NodeStatus node) => node?.Services != null && node.Services.Any(service => !service.IsRunning);
        private static string Explain(NodeStatus node, string fallback) =>
            !string.IsNullOrWhiteSpace(node?.Details) ? node.Details : !string.IsNullOrWhiteSpace(node?.StatusText) ? node.StatusText : fallback;
    }
}

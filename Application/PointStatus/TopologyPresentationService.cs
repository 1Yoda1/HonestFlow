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
            EsmStatusDto status = result.EsmApiStatus?.Status;
            EsmStatusDto api = status?.Software?.Data ?? status?.Data?.Software?.Data ?? status?.Data ?? status;
            return new TopologyPresentation
            {
                LmFrame = FrameFromServices(result.Lm),
                EsmFrame = FrameFromServices(result.Esm),
                KktFrame = FrameFromServices(result.Kkt),
                ControllerFrame = FrameFromNode(result.Controller),
                CloudToEsm = LinkFromCloud(result.Cloud),
                EsmToController = LinkFromEsmInstance(result, LinkFromLmInfoCode(result.EsmApiStatus?.Status?.LmInfo, "ЕСМ ↔ Controller")),
                ControllerToLm = LinkFromEsmInstance(result, LinkFromCode(api?.Lm, "Связь с ЛМ")),
                EsmToKkt = LinkFromEsmInstance(result, LinkFromCashRegister(result.CashRegister))
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

        private static TopologyVisualState FrameFromNode(NodeStatus node) => node?.Level switch
        {
            NodeLevel.Ok => TopologyVisualState.Healthy,
            NodeLevel.Error => TopologyVisualState.Missing,
            _ => TopologyVisualState.Uncertain
        };

        private static TopologyLinkPresentation LinkFromCode(EsmComponentStatus status, string name) =>
            status?.Code == 0
                ? new TopologyLinkPresentation(TopologyVisualState.Healthy, $"{name}: код 0.")
                : status?.Code.HasValue == true
                    ? new TopologyLinkPresentation(TopologyVisualState.Missing, $"{name}: код {status.Code}.")
                    : new TopologyLinkPresentation(TopologyVisualState.Uncertain, $"{name}: данные отсутствуют.");

        private static TopologyLinkPresentation LinkFromLmInfoCode(EsmLmInfoDto status, string name) =>
            status?.EffectiveCode == 0
                ? new TopologyLinkPresentation(TopologyVisualState.Healthy, $"{name}: код 0.")
                : status?.EffectiveCode.HasValue == true
                    ? new TopologyLinkPresentation(TopologyVisualState.Missing, $"{name}: код {status.EffectiveCode}.")
                    : new TopologyLinkPresentation(TopologyVisualState.Uncertain, $"{name}: данные отсутствуют.");

        private static TopologyLinkPresentation LinkFromCashRegister(EsmCashRegisterResult result) => result?.Kind switch
        {
            EsmCashRegisterResultKind.Connected => new TopologyLinkPresentation(TopologyVisualState.Healthy, "ЕСМ видит подключённую ККТ."),
            EsmCashRegisterResultKind.Disconnected => new TopologyLinkPresentation(TopologyVisualState.Missing, "ЕСМ не видит ККТ."),
            _ => new TopologyLinkPresentation(TopologyVisualState.Uncertain, "Связь ЕСМ с ККТ не проверена.")
        };

        private static TopologyLinkPresentation LinkFromEsmInstance(
            PointStatusResult result,
            TopologyLinkPresentation link)
        {
            if (result?.EsmApiStatus?.Kind != EsmStatusResultKind.Success ||
                result.EsmRegistration?.Kind != EsmRegistrationResultKind.Registered)
                return new TopologyLinkPresentation(TopologyVisualState.Uncertain, "Связь не проверена: ЕСМ не зарегистрирован или его API недоступен.");
            return link;
        }

        private static TopologyLinkPresentation LinkFromCloud(NodeStatus cloud)
        {
            if (cloud?.Level == NodeLevel.Ok)
                return new TopologyLinkPresentation(TopologyVisualState.Healthy, "Связь с облаком установлена.");
            return new TopologyLinkPresentation(TopologyVisualState.Uncertain, Explain(cloud, "Не удалось подтвердить связь с облаком."));
        }

        private static string Explain(NodeStatus node, string fallback) =>
            !string.IsNullOrWhiteSpace(node?.Details) ? node.Details :
            !string.IsNullOrWhiteSpace(node?.StatusText) ? node.StatusText : fallback;

    }
}

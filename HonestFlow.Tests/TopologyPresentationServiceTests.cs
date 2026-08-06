using HonestFlow.Application.PointStatus;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class TopologyPresentationServiceTests
    {
        private readonly TopologyPresentationService _service = new();

        [Fact]
        public void RunningServicesAndHealthyChecks_ProduceGreenFramesAndLinks()
        {
            TopologyPresentation result = _service.Create(CreateResult(Healthy("lm"), Healthy("controller"), Healthy("esm"), Healthy("kkt"), Cloud(NodeLevel.Ok)));
            Assert.Equal(TopologyVisualState.Healthy, result.LmFrame);
            Assert.Equal(TopologyVisualState.Healthy, result.EsmFrame);
            Assert.Equal(TopologyVisualState.Healthy, result.KktFrame);
            Assert.Equal(TopologyVisualState.Healthy, result.CloudToEsm.State);
            Assert.Equal(TopologyVisualState.Healthy, result.ControllerToLm.State);
        }

        [Fact]
        public void StoppedService_ProducesRedFrameButYellowConnection()
        {
            NodeStatus stopped = Node(NodeLevel.Error, new ServiceSnapshot("lm", "Stopped"));
            TopologyPresentation result = _service.Create(CreateResult(stopped, Healthy("controller"), Healthy("esm"), Healthy("kkt"), Cloud(NodeLevel.Ok)));
            Assert.Equal(TopologyVisualState.Missing, result.LmFrame);
            Assert.Equal(TopologyVisualState.Uncertain, result.ControllerToLm.State);
        }

        [Fact]
        public void MissingComponent_ProducesRedCrossConnection()
        {
            NodeStatus missing = new(NodeLevel.Error, "Не установлен", "Компонент отсутствует");
            TopologyPresentation result = _service.Create(CreateResult(missing, Healthy("controller"), Healthy("esm"), Healthy("kkt"), Cloud(NodeLevel.Ok)));
            Assert.Equal(TopologyVisualState.Missing, result.ControllerToLm.State);
            Assert.Contains("отсутствует", result.ControllerToLm.Explanation);
        }

        [Fact]
        public void AccountingSystem_IsAlwaysIgnored()
        {
            TopologyPresentation result = _service.Create(CreateResult(Healthy("lm"), Healthy("controller"), Healthy("esm"), Healthy("kkt"), Cloud(NodeLevel.Ok)));
            Assert.Equal(TopologyVisualState.Ignored, result.AccountingToEsm.State);
        }

        private static PointStatusResult CreateResult(NodeStatus lm, NodeStatus controller, NodeStatus esm, NodeStatus kkt, NodeStatus cloud) => new()
        {
            Lm = lm,
            Controller = controller,
            Esm = esm,
            Kkt = kkt,
            Cloud = cloud,
            RuDesktop = Node(NodeLevel.Ok, new ServiceSnapshot("RuDesktop", "Running"))
        };

        private static NodeStatus Healthy(string name) => Node(NodeLevel.Ok, new ServiceSnapshot(name, "Running"));
        private static NodeStatus Cloud(NodeLevel level) => new(level, level == NodeLevel.Ok ? "Доступно" : "Ошибка", "Проверка облака");
        private static NodeStatus Node(NodeLevel level, params ServiceSnapshot[] services) => new(level, level.ToString(), "details", services);
    }
}

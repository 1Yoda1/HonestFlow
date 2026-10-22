using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HonestFlow.Application.PointStatus;
using HonestFlow.Models;
using HonestFlow.UI;
using Xunit;

namespace HonestFlow.Tests;

// Render the real compiled window, but deliberately use its inert constructor:
// no App startup, update check, identity, probes, services or installation handlers.
[Collection("MainWindow WPF fixtures")]
public sealed class MainWindowLayoutRenderingTests
{
    [Fact]
    public void MainWindow_FixtureLayoutsFit_AndTopologyConnectionsStayAnchored()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try { RenderCases(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "WPF fixture rendering timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void RenderCases()
    {
        foreach (bool service in new[] { false, true })
        foreach (bool minimum in new[] { false, true })
        {
            var window = new MainWindow();
            try
            {
                Set(window, "_paidExecutionEnabled", service);
                Set(window, "_client", new IPData { Name = "Тестовая торговая точка — длинное название организации" });
                Invoke(window, service ? "ApplyServicePresentation" : "ApplyFreePresentation");
                Control<TextBlock>(window, "ApplicationVersionText").Text = $"HonestFlow {(service ? "Service" : "Free")} v3.0.0";
                Control<TextBlock>(window, "ClientDetailsText").Text = service ? "Device ID: test-device" : "TEST-WORKPLACE";
                var root = (FrameworkElement)window.Content;
                double width = minimum ? 1178 : 1364;
                double height = minimum ? 678 : 718;
                string prefix = $"{(service ? "service" : "free")}-{(minimum ? "minimum" : "standard")}";

                foreach (string state in new[] { "healthy", "errors", "kkt-error" })
                {
                    bool healthy = state == "healthy";
                    var snapshot = FixtureSnapshot(healthy, state == "kkt-error");
                    Set(window, "_lastDiagnostics", snapshot);
                    Invoke(window, "ShowDetailedView");
                    Invoke(window, "ApplyTopology", new TopologyPresentationService().Create(snapshot));
                    Invoke(window, "ApplySimpleStatus", new TopologyPresentationService().Create(snapshot));
                    Invoke(window, "ApplyDetailedProblems", new DiagnosticIssuePresentationMapper().Create(snapshot));
                    Invoke(window, "UpdateInstructionAvailability");
                    if (!healthy)
                    {
                        Control<TextBlock>(window, "EsmNodeStatusText").Text = "ЕСМ не зарегистрирован";
                        Control<TextBlock>(window, "KktNodeStatusText").Text = "ККТ физически не подключена";
                    }
                    Layout(root, width, height);
                    VerifyTopology(window, root);
                    AssertContained(Control<Border>(window, "DetailedProblemPanel"), root);
                    Assert.Equal(service ? Visibility.Visible : Visibility.Collapsed,
                        Control<Button>(window, "DetailedFixButton").Visibility);
                    foreach (double scale in new[] { 1.0, 1.25, 1.5 })
                        Render(root, FormattableString.Invariant($"{prefix}-{state}-{scale:0.00}"), scale);
                }

                Invoke(window, "ShowSimpleView");
                Layout(root, width, height);
                AssertContained(Control<TextBlock>(window, "SimpleStatusTitle"), root);
                Render(root, prefix + "-simple", 1);

                ShowSection(window, "Components", "Компоненты", "Установленные и требуемые версии компонентов.", service);
                Control<DataGrid>(window, "ComponentVersionsGrid").ItemsSource = new[]
                {
                    new ComponentFixture("ЛМ ЧЗ", "1.11.0", service),
                    new ComponentFixture("Контроллер ЛМ", "1.4.6", service),
                    new ComponentFixture("ТС ПИоТ (ЕСМ)", "1.6.3.2", service),
                    new ComponentFixture("Драйвер ККТ АТОЛ", "10.10.8.23", service),
                    new ComponentFixture("ККТ gRPC", "1.0.0", service)
                };
                Layout(root, width, height);
                AssertContained(Control<DataGrid>(window, "ComponentVersionsGrid"), root);
                Assert.True(Control<DataGrid>(window, "ComponentVersionsGrid").Columns[0].ActualWidth > 120);
                Render(root, prefix + "-components", 1);

                ShowSection(window, "Diagnostics", "Инструменты ручного ремонта", "Папки, службы и системные средства для обслуживания рабочей точки.", false);
                Control<DataGrid>(window, "ServiceToolsGrid").ItemsSource = new[]
                {
                    new ServiceFixture("ЕСМ — Orchestrator", "esm-orchestrator", "Запущена", service),
                    new ServiceFixture("Контроллер ЛМ", "esm-lm-controller", "Запущена", service),
                    new ServiceFixture("ЛМ ЧЗ — Regime", "regime", "Остановлена", service)
                };
                Layout(root, width, height);
                AssertContained(Control<ScrollViewer>(window, "ToolsScrollViewer"), root);
                Render(root, prefix + "-tools", 1);
                Control<ScrollViewer>(window, "ToolsScrollViewer").ScrollToVerticalOffset(180);
                Layout(root, width, height);
                Render(root, prefix + "-services", 1);
            }
            finally { window.Close(); }
        }
    }

    private static void VerifyTopology(MainWindow window, FrameworkElement root)
    {
        var nodes = new[] { "Gismt", "Esm", "Controller", "Lm", "Kkt" }
            .Select(n => Control<Border>(window, n + "NodeBorder")).ToArray();
        Rect[] bounds = nodes.Select(n => Bounds(n, root)).ToArray();
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(bounds[0].Top, bounds[i].Top, 1);
            Assert.Equal(bounds[0].Height, bounds[i].Height, 1);
            AssertContained(nodes[i], root);
            if (i > 0) Assert.True(bounds[i].Left > bounds[i - 1].Right);
        }
        Assert.Equal(bounds[1].Left, bounds[4].Left, 1);
        Assert.Equal(bounds[1].Width, bounds[4].Width, 1);
        Assert.True(bounds[4].Top > bounds[1].Bottom);
        foreach (var edge in new[] { ("CloudEsm", 0, 1), ("EsmController", 1, 2), ("ControllerLm", 2, 3), ("EsmKkt", 1, 4) })
        {
            var line = Control<System.Windows.Shapes.Line>(window, edge.Item1 + "Line");
            Rect lineBounds = Bounds(line, root);
            bool vertical = edge.Item1 == "EsmKkt";
            AssertNear(vertical ? bounds[edge.Item2].Bottom : bounds[edge.Item2].Right,
                vertical ? lineBounds.Top : lineBounds.Left);
            AssertNear(vertical ? bounds[edge.Item3].Top : bounds[edge.Item3].Left,
                vertical ? lineBounds.Bottom : lineBounds.Right);
            var marker = Control<Border>(window, edge.Item1 + "Marker");
            if (marker.Visibility == Visibility.Visible)
            {
                Rect markerBounds = Bounds(marker, root);
                AssertNear(lineBounds.Left + lineBounds.Width / 2, markerBounds.Left + markerBounds.Width / 2);
                AssertNear(lineBounds.Top + lineBounds.Height / 2, markerBounds.Top + markerBounds.Height / 2);
                Assert.False(bounds[edge.Item2].IntersectsWith(markerBounds));
                Assert.False(bounds[edge.Item3].IntersectsWith(markerBounds));
            }
        }
        foreach (string name in new[] { "Gismt", "Esm", "Controller", "Lm", "Kkt" })
        {
            var text = Control<TextBlock>(window, name + "NodeStatusText");
            Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
            Rect card = Bounds(Control<Border>(window, name + "NodeBorder"), root);
            Assert.True(card.Contains(Bounds(text, root)), name + " status clipped");
            Assert.Equal(Control<Border>(window, name + "NodeBorder").BorderBrush.ToString(),
                Control<Border>(window, name + "NodeIcon").Background.ToString());
        }
    }

    private static DiagnosticsSnapshot FixtureSnapshot(bool healthy, bool kktError)
    {
        var ready = new DiagnosticComponentFact(DiagnosticState.Healthy, "Доступно");
        var failed = new DiagnosticComponentFact(DiagnosticState.Failed, "Служба остановлена");
        var link = new DiagnosticConnectionFact(healthy ? DiagnosticConnectionState.Connected : DiagnosticConnectionState.Unknown, "");
        return new DiagnosticsSnapshot
        {
            Esm = healthy ? ready : failed, Controller = ready, Lm = healthy ? ready : failed,
            Kkt = healthy ? ready : kktError ? failed : new DiagnosticComponentFact(DiagnosticState.Unknown, "ККТ не подключена"),
            Gismt = healthy ? ready : new DiagnosticComponentFact(DiagnosticState.Unknown, "Нет данных"),
            GismtToEsm = link, EsmToController = link, EsmToKkt = link, LmConnection = link,
            WorkState = healthy ? WorkState.Ready : WorkState.WorkImpossible,
            Issues = healthy ? Array.Empty<DiagnosticIssue>() : new[]
            {
                new DiagnosticIssue(DiagnosticIssueCode.ESM_NOT_REGISTERED, DiagnosticComponent.Esm,
                    DiagnosticSeverity.WorkImpossible, "ЕСМ не зарегистрирован", "Необходимо зарегистрировать ТС ПИоТ.",
                    "Fixture only", Array.Empty<DiagnosticEvidence>())
            }
        };
    }

    private static void ShowSection(MainWindow window, string section, string title, string description, bool actions)
    {
        Type sectionType = typeof(MainWindow).GetNestedType("Section", BindingFlags.NonPublic);
        Invoke(window, "ShowSection", Enum.Parse(sectionType, section), title, description,
            actions ? "Обновить всё" : null, actions ? "Переустановить" : null,
            actions ? "Восстановить ЛМ ЧЗ" : null, null);
    }

    private static void Layout(FrameworkElement root, double width, double height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        root.UpdateLayout();
    }
    private static Rect Bounds(FrameworkElement item, FrameworkElement root) =>
        item.TransformToAncestor(root).TransformBounds(new Rect(item.RenderSize));
    private static void AssertContained(FrameworkElement item, FrameworkElement root) =>
        Assert.True(new Rect(root.RenderSize).Contains(Bounds(item, root)), item.Name + " outside window");
    private static void AssertNear(double expected, double actual) => Assert.InRange(actual, expected - 2, expected + 2);
    private static T Control<T>(MainWindow window, string name) => (T)window.FindName(name);
    private static void Set(MainWindow window, string name, object value) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, value);
    private static void Invoke(MainWindow window, string name, params object[] args) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, args);

    private static void Render(FrameworkElement root, string name, double scale)
    {
        // Optional local evidence; tests always measure/assert without writing artifacts.
        string output = Environment.GetEnvironmentVariable("HONESTFLOW_TEST_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * scale),
            (int)Math.Ceiling(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(stream);
    }

    public sealed record ComponentFixture(string Name, string InstalledVersion, bool CanUpdate)
    {
        public string ExpectedVersion => InstalledVersion;
        public string MatchText => "Да";
        public string ActionText => "Переустановить";
    }
    public sealed record ServiceFixture(string DisplayName, string ServiceName, string Status, bool CanControl);
}

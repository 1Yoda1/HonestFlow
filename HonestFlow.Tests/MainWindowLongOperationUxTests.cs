using System;
using System.IO;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class MainWindowLongOperationUxTests
    {
        [Fact]
        public void UpdateAll_ReinstallAndLmRestore_UseSameProgressPresentationMethod()
        {
            string source = File.ReadAllText(ProjectFile("HonestFlow.WpfPrototype", "MainWindow.xaml.cs"));

            Assert.Contains("RunInstallationOperationAsync(",
                Segment(source, "private async Task InstallAllAsync", "private async Task RunInstallationOperationAsync"));
            Assert.Contains("RunInstallationOperationAsync(",
                Segment(source, "private async void UpdateComponent_Click", "private async Task ReinstallSelectedAsync"));
            Assert.Contains("RunInstallationOperationAsync(",
                Segment(source, "private async Task RestoreLmDatabaseAsync", "private async Task RefreshLicenseAsync"));
        }

        [Fact]
        public void UpdateAll_RegistersTsPiotAfterSuccessfulComponentInstallation()
        {
            string source = File.ReadAllText(ProjectFile("HonestFlow.WpfPrototype", "MainWindow.xaml.cs"));
            string installAll = Segment(source, "private async Task InstallAllAsync", "private async Task RunInstallationOperationAsync");

            Assert.Contains("InstallAndRegisterTsPiotAsync", installAll);
            Assert.Contains("FormatInstallationCompletionStatus", installAll);
            Assert.Contains("new TsPiotRegistrationWorkflow", source);
            Assert.Contains("new EsmTsPiotRegistrationClient", source);
        }

        [Fact]
        public void SharedPresentation_BlocksParallelStart_EndsWithAcknowledgementAndReleasesBusyState()
        {
            string source = File.ReadAllText(ProjectFile("HonestFlow.WpfPrototype", "MainWindow.xaml.cs"));
            string operation = Segment(source,
                "private async Task RunInstallationOperationAsync",
                "private async Task RefreshAfterComponentOperationAsync");

            Assert.Contains("if (_operationRunning) return;", operation);
            Assert.Contains("ShowInstallationProgress(title, initialStatus);", operation);
            Assert.Contains("WaitForInstallationAcknowledgementAsync", operation);
            Assert.Contains("catch (Exception ex)", operation);
            Assert.Contains("finally", operation);
            Assert.Contains("_operationRunning = false;", operation);
        }

        [Fact]
        public void SharedPresentation_RefreshesVersionsAndDiagnosticsAfterOperation()
        {
            string source = File.ReadAllText(ProjectFile("HonestFlow.WpfPrototype", "MainWindow.xaml.cs"));
            string refresh = Segment(source,
                "private async Task RefreshAfterComponentOperationAsync",
                "private void ShowInstallationProgress");

            Assert.Contains("LoadComponentVersionsAsync", refresh);
            Assert.Contains("RefreshTopologyAsync(allowDuringOperation: true)", refresh);

            string topologyRefresh = Segment(source,
                "private async Task RefreshTopologyAsync",
                "private async Task<DiagnosticsSnapshot> RefreshDeveloperDiagnosticsAsync");
            Assert.Contains("(_operationRunning && !allowDuringOperation)", topologyRefresh);
        }

        [Fact]
        public void ProgressOverlay_HasReusableOperationTitle()
        {
            string xaml = File.ReadAllText(ProjectFile("HonestFlow.WpfPrototype", "MainWindow.xaml"));

            Assert.Contains("x:Name=\"InstallationBusyOverlay\"", xaml);
            Assert.Contains("x:Name=\"InstallationProgressTitle\"", xaml);
        }

        [Fact]
        public void ServiceTools_UseApplicationServiceAndRefreshGridAfterCompletedAction()
        {
            string source = File.ReadAllText(ProjectFile("HonestFlow.WpfPrototype", "MainWindow.xaml.cs"));
            string control = Segment(source,
                "private async Task ControlServiceFromButtonAsync",
                "private static string ServiceActionText");

            Assert.Contains("new WindowsServiceControlService", control);
            Assert.Contains("await RefreshServiceToolsAsync();", control);
            Assert.DoesNotContain("new ServiceController", control);
        }

        [Fact]
        public void AutoFixButton_UsesRealWorkflowWithBusyStateAndConfirmationContinuation()
        {
            string source = File.ReadAllText(ProjectFile("HonestFlow.WpfPrototype", "MainWindow.xaml.cs"));
            string autoFix = Segment(source,
                "public async Task StartAutoFixAsync()",
                "private AutoFixWorkflow CreateAutoFixWorkflow()");
            string composition = Segment(source,
                "private AutoFixWorkflow CreateAutoFixWorkflow()",
                "private void SetAutoFixProgress");
            string xaml = File.ReadAllText(ProjectFile("HonestFlow.WpfPrototype", "MainWindow.xaml"));
            string sharedComposition = File.ReadAllText(ProjectFile("HonestFlow.WpfPrototype", "WpfAutoFixComposition.cs"));

            Assert.Contains("if (_operationRunning || _autoFixWorkflow == null) return;", autoFix);
            Assert.Contains("_operationRunning = true;", autoFix);
            Assert.Contains("_operationRunning = false;", autoFix);
            Assert.Contains("_autoFixWorkflow.RunAsync", autoFix);
            Assert.Contains("_autoFixWorkflow.ContinueAsync", autoFix);
            Assert.Contains("ApplyPointStatusRefresh", autoFix);
            Assert.Contains("WpfAutoFixComposition.Create", composition);
            Assert.Contains("new AutoFixPlanner()", sharedComposition);
            Assert.Contains("new AutoFixExecutor(actions)", sharedComposition);
            Assert.Contains("x:Name=\"SimpleFixButton\"", xaml);
            Assert.Contains("x:Name=\"DetailedFixButton\"", xaml);
            Assert.Contains("x:Name=\"AutoFixHistoryList\"", xaml);
            Assert.Equal(2, xaml.Split("Click=\"SimpleFix_Click\"").Length - 1);
        }

        private static string Segment(string source, string startMarker, string endMarker)
        {
            int start = source.IndexOf(startMarker, StringComparison.Ordinal);
            int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Marker not found: {startMarker}");
            Assert.True(end > start, $"Marker not found after start: {endMarker}");
            return source.Substring(start, end - start);
        }

        private static string ProjectFile(params string[] parts)
        {
            string path = AppContext.BaseDirectory;
            for (int depth = 0; depth < 8; depth++)
            {
                string candidate = Path.Combine(path, Path.Combine(parts));
                if (File.Exists(candidate)) return candidate;
                path = Directory.GetParent(path)?.FullName ?? path;
            }
            throw new FileNotFoundException("Project file not found", Path.Combine(parts));
        }
    }
}

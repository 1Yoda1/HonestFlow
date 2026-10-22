using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using HonestFlow.Application.Bootstrap;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Ui;
using HonestFlow.Models;
using HonestFlow.UI;
using Xunit;

namespace HonestFlow.Tests;

// Inert windows only: no App startup, network, services, installers or real probes.
[Collection("MainWindow WPF fixtures")]
public sealed class MainWindowRuntimeRegressionTests
{
    [Theory]
    [InlineData("Components")]
    [InlineData("Topology")]
    [InlineData("Tools")]
    public async Task RuntimeSwitch_DiscardsGatedOldResultAndError(string view)
    {
        var tracker = new RuntimeRefreshTracker();
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        RuntimeRefreshTracker.Request old = tracker.Begin(view);
        string displayed = null;
        Task pending = ApplyAsync();
        tracker.Advance();
        RuntimeRefreshTracker.Request current = tracker.Begin(view);
        Assert.True(current.IsCurrent);
        displayed = "new runtime";
        gate.SetException(new InvalidOperationException("old error"));
        await pending;
        Assert.Equal("new runtime", displayed);
        Assert.False(old.IsCurrent);

        async Task ApplyAsync()
        {
            try
            {
                string value = await gate.Task;
                if (old.IsCurrent) displayed = value;
            }
            catch (Exception ex)
            {
                if (old.IsCurrent) displayed = ex.Message;
            }
        }
    }

    [Fact]
    public void NewRequest_SupersedesOnlyItsOwnView_AndCloseInvalidatesAll()
    {
        var tracker = new RuntimeRefreshTracker();
        RuntimeRefreshTracker.Request old = tracker.Begin("Components");
        RuntimeRefreshTracker.Request topology = tracker.Begin("Topology");
        RuntimeRefreshTracker.Request newest = tracker.Begin("Components");
        Assert.False(old.IsCurrent);
        Assert.True(topology.IsCurrent);
        Assert.True(newest.IsCurrent);
        long generation = tracker.Generation;
        tracker.Advance();
        Assert.False(tracker.IsCurrent(generation));
        Assert.False(topology.IsCurrent);
        Assert.False(newest.IsCurrent);
    }

    [Fact]
    public Task Components_LateOldRuntimeCannotOverwriteNewRows() => OnSta(async () =>
    {
        var window = new MainWindow();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task old = null;
        try
        {
            Set(window, "_applicationMode", ApplicationMode.Free);
            Set(window, "_componentVersionStatusService", new ComponentVersionStatusService(
                new StubVersions(), () =>
                {
                    entered.TrySetResult();
                    return release.Task.GetAwaiter().GetResult();
                }));
            old = Invoke<Task>(window, "LoadComponentVersionsAsync");
            await entered.Task;
            Get<RuntimeRefreshTracker>(window, "_runtimeRefreshes").Advance();
            Set(window, "_componentVersionStatusService", new ComponentVersionStatusService(new StubVersions(), () => "2.0"));
            await Invoke<Task>(window, "LoadComponentVersionsAsync");
            release.SetResult("1.0");
            await old;
            Assert.Equal("2.0", Get<ComponentVersionStatus[]>(window, "_componentStatuses")[0].InstalledVersion);
        }
        finally
        {
            release.TrySetResult("1.0");
            window.Close();
            if (old != null) await old;
        }
    });

    [Fact]
    public Task Close_CompletesPendingAcknowledgement_AndDoesNotCreateAnother() => OnSta(async () =>
    {
        var window = new MainWindow();
        Task acknowledgement = Invoke<Task>(window, "WaitForInstallationAcknowledgementAsync");
        Assert.False(acknowledgement.IsCompleted);
        window.Close();
        await acknowledgement;
        Assert.True(Invoke<Task>(window, "WaitForInstallationAcknowledgementAsync").IsCompleted);
    });

    [Theory]
    [InlineData("explicit cancellation", "cancelled")]
    [InlineData("timeout", "timeout")]
    [InlineData("failure", "failed")]
    public Task InstallResult_DistinguishesCallerTokenCancellationFromTimeout(string outcome, string expected) => OnSta(async () =>
    {
        var window = new MainWindow();
        try
        {
            Set(window, "_paidExecutionEnabled", true);
            Func<CancellationToken, Task<bool>> operation = token => outcome switch
            {
                "explicit cancellation" => Task.FromException<bool>(new OperationCanceledException("declined", token)),
                "timeout" => Task.FromException<bool>(new TaskCanceledException("timeout", null, new CancellationToken(true))),
                _ => Task.FromResult(false)
            };
            Task pending = RunInstallation(window, operation);
            Assert.Equal(expected, ((TextBlock)window.FindName("InstallationProgressText")).Text);
            Get<TaskCompletionSource>(window, "_installationCompletion").TrySetResult();
            await pending;
            Assert.False(Get<bool>(window, "_operationRunning"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task PaidInstallation_ReceivesLinkedServiceLifetime_AndCloseReleasesOperation() => OnSta(async () =>
    {
        var window = new MainWindow();
        using var serviceLifetime = new CancellationTokenSource();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captured = default;
        try
        {
            Set(window, "_paidExecutionEnabled", true);
            Set(window, "_serviceLifetime", serviceLifetime);
            Task pending = RunInstallation(window, token =>
            {
                captured = token;
                return release.Task;
            });
            Assert.True(captured.CanBeCanceled);
            serviceLifetime.Cancel();
            Assert.True(captured.IsCancellationRequested);
            window.Close();
            release.TrySetCanceled(captured);
            await pending;
            Assert.False(Get<bool>(window, "_operationRunning"));
        }
        finally
        {
            release.TrySetCanceled();
            window.Close();
        }
    });

    [Fact]
    public void ActivationAndDisconnect_KeepCapturedLifetimeAndResetFreeBeforeLogoutAwait()
    {
        string source = ReadMainWindow();
        string activation = Segment(source, "internal async Task ActivateServiceAsync", "private async Task DeactivateServiceAsync");
        Assert.True(activation.IndexOf("CancellationToken serviceToken = _serviceLifetime.Token;", StringComparison.Ordinal) <
            activation.IndexOf("await RefreshCurrentSectionAfterRuntimeChangeAsync();", StringComparison.Ordinal));
        Assert.Contains("IsCurrentRuntime(generation) && !serviceToken.IsCancellationRequested", activation);
        Assert.Contains("RunPeriodicLicenseRefreshAsync(serviceToken)", activation);
        string disconnect = Segment(source, "private async Task DeactivateServiceAsync", "private async Task RefreshCurrentSectionAfterRuntimeChangeAsync");
        Assert.True(disconnect.IndexOf("_applicationMode = ApplicationMode.Free", StringComparison.Ordinal) <
            disconnect.IndexOf("await service.Controller.LogoutAsync", StringComparison.Ordinal));
        Assert.Contains("_serviceDisconnectRunning = true", disconnect);
        Assert.Contains("_serviceDisconnectRunning = false", disconnect);
        string refresh = Segment(source, "private async Task RefreshCurrentSectionAfterRuntimeChangeAsync", "private void ShowToolError");
        Assert.Contains("RefreshTopologyAsync(allowDuringOperation: true)", refresh);
        Assert.Contains("await topologyRefresh", refresh);
    }

    [Fact]
    public void CleanupAndUpload_DemandLiveGuardAfterYieldAtMutationBoundary()
    {
        string source = ReadMainWindow();
        string cleanup = Segment(source, "private void RemoveEsmGuiDuplicates_Click", "private static DateTime TryGetStartTime");
        int guard = cleanup.IndexOf("CreateLicenseGuard().Demand(LicenseOperation.OpenLocalTools)", StringComparison.Ordinal);
        Assert.True(guard > cleanup.IndexOf("MessageBoxButton.YesNo", StringComparison.Ordinal));
        Assert.True(guard < cleanup.IndexOf("process.Kill", StringComparison.Ordinal));
        Assert.Contains("if (!IsCurrentRuntime(generation)) return;", cleanup);
        string upload = Segment(source, "private async Task RunDiagnosticsAsync", "private async Task InstallAsync");
        guard = upload.IndexOf("CreateLicenseGuard().Demand(LicenseOperation.SendDiagnostics)", StringComparison.Ordinal);
        Assert.True(guard > upload.IndexOf("await workflow.CreateArchiveAsync", StringComparison.Ordinal));
        Assert.True(guard < upload.IndexOf("await workflow.SendArchiveAsync", StringComparison.Ordinal));
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested();", upload);
    }

    private static Task RunInstallation(MainWindow window, Func<CancellationToken, Task<bool>> operation) =>
        Invoke<Task>(window, "RunInstallationOperationAsync", "test", "starting", operation, "success", "failed", "cancelled", null);

    private static Task OnSta(Func<Task> test)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await test(); completed.TrySetResult(); }
                catch (Exception ex) { completed.TrySetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static T Get<T>(MainWindow window, string name) =>
        (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
    private static void Set(MainWindow window, string name, object value) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, value);
    private static T Invoke<T>(MainWindow window, string name, params object[] arguments) =>
        (T)typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, arguments);

    private static string Segment(string source, string start, string end)
    {
        int first = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(first >= 0);
        int last = source.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        Assert.True(last > first);
        return source.Substring(first, last - first);
    }

    private static string ReadMainWindow()
    {
        string directory = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8; depth++)
        {
            string path = Path.Combine(directory, "UI", "MainWindow.xaml.cs");
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = Directory.GetParent(directory)?.FullName ?? directory;
        }
        throw new FileNotFoundException("MainWindow.xaml.cs");
    }

    private sealed class StubVersions : IVersionCheckService
    {
        public bool NeedAtolInstall(IPData client, string version) => false;
        public bool NeedEsmInstall(string version) => false;
        public bool NeedControllerInstall(string version) => false;
        public string GetAtolDriverInfo() => "10.10.8.24 (64-bit)";
        public string GetAtolDriverInfo(string architecture) => GetAtolDriverInfo();
        public string GetEsmVersion() => "1.0";
        public string GetControllerVersion() => "1.0";
    }
}

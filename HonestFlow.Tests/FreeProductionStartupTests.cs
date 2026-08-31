using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Bootstrap;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Infrastructure.Updates;
using Xunit;

namespace HonestFlow.Tests;

public sealed class FreeProductionStartupTests
{
    [Fact]
    public async Task PublicSelfUpdate_RunsBeforeFreeComposition()
    {
        var events = new List<string>();
        var startup = new FreeApplicationStartup(
            _ =>
            {
                events.Add("update");
                return Task.FromResult(false);
            },
            () =>
            {
                events.Add("local");
                return new LocalApplicationBootstrap().Create();
            });

        FreeApplicationStartupResult result = await startup.StartAsync(CancellationToken.None);

        Assert.False(result.UpdateStarted);
        Assert.Equal(ApplicationMode.Free, result.Context.Mode);
        Assert.Equal(new[] { "update", "local" }, events);
    }

    [Fact]
    public async Task StartedUpdate_DoesNotComposeFreeWindowContext()
    {
        bool localCompositionCalled = false;
        var startup = new FreeApplicationStartup(
            _ => Task.FromResult(true),
            () =>
            {
                localCompositionCalled = true;
                return new LocalApplicationBootstrap().Create();
            });

        FreeApplicationStartupResult result = await startup.StartAsync(CancellationToken.None);

        Assert.True(result.UpdateStarted);
        Assert.Null(result.Context);
        Assert.False(localCompositionCalled);
    }

    [Fact]
    public async Task UnavailableUpdateEndpoint_ContinuesWithFreeComposition()
    {
        var update = new SelfUpdateService(
            new UnavailableUpdateClient(),
            new SilentDialogs());
        var startup = new FreeApplicationStartup(
            update.CheckDownloadAndRunUpdateIfNeeded,
            new LocalApplicationBootstrap().Create);

        FreeApplicationStartupResult result = await startup.StartAsync(CancellationToken.None);

        Assert.False(result.UpdateStarted);
        Assert.NotNull(result.Context);
    }

    [Fact]
    public void ProductionApp_HasNoAutomaticStartupWindow()
    {
        string appXaml = File.ReadAllText(ProjectFile("App.xaml"));
        string appCode = File.ReadAllText(ProjectFile("App.xaml.cs"));
        string startupWindow = File.ReadAllText(ProjectFile("UI", "StartupWindow.xaml.cs"));

        Assert.DoesNotContain("StartupUri", appXaml);
        Assert.Contains("new FreeApplicationStartup(", appCode);
        Assert.Contains("new MainWindow(result.Context)", appCode);
        Assert.DoesNotContain("new StartupWindow", appCode);
        Assert.DoesNotContain("SelfUpdateService", startupWindow);
    }

    [Fact]
    public void FreeComposition_HasNoAuthSessionDeviceLicenseOrConfigurationDependency()
    {
        string bootstrap = File.ReadAllText(ProjectFile("Application", "Bootstrap", "LocalApplicationBootstrap.cs"));
        string app = File.ReadAllText(ProjectFile("App.xaml.cs"));
        string productionComposition = bootstrap + app;

        Assert.DoesNotContain("ApiAuthService", productionComposition);
        Assert.DoesNotContain("ApiSessionService", productionComposition);
        Assert.DoesNotContain("ApiSessionStore", productionComposition);
        Assert.DoesNotContain("FileDeviceIdentityService", productionComposition);
        Assert.DoesNotContain("LicenseObservation", productionComposition);
        Assert.DoesNotContain("configuration/current", productionComposition, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("license/current", productionComposition, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MainWindowFree_UsesLocalDiagnosticsAndLocksServiceActions()
    {
        string code = File.ReadAllText(ProjectFile("UI", "MainWindow.xaml.cs"));
        string xaml = File.ReadAllText(ProjectFile("UI", "MainWindow.xaml"));

        Assert.Contains("public MainWindow(LocalApplicationContext context)", code);
        Assert.Contains("await RefreshTopologyAsync();", code);
        Assert.Contains("RefreshLocalAsync(", code);
        Assert.Contains("GetLocalStatuses(", code);
        Assert.Contains("_localDiagnosticArchive.CreateArchiveInfo(selection, pointAddress: null)", code);
        Assert.Contains("x:Name=\"ConnectServiceButton\"", xaml);
        Assert.Contains("x:Name=\"SendDiagnosticsButton\"", xaml);
        Assert.Contains("x:Name=\"ServiceControlColumn\"", xaml);
        Assert.Contains("x:Name=\"ExpectedVersionColumn\"", xaml);
        Assert.Contains("x:Name=\"VersionMatchColumn\"", xaml);

        string freePresentation = Segment(code, "private void ApplyFreePresentation()", "public MainWindow(StartupResult");
        foreach (string control in new[]
        {
            "LogoutButton", "RateButton", "RefreshLicenseButton", "FooterHelpButton",
            "SimpleHelpButton", "SimpleFixButton", "DetailedFixButton", "SendDiagnosticsButton",
            "ServiceControlColumn", "ExpectedVersionColumn", "VersionMatchColumn"
        })
        {
            Assert.Contains($"{control}.Visibility = Visibility.Collapsed", freePresentation);
        }
        Assert.Contains("ConnectServiceButton.Visibility = Visibility.Visible", freePresentation);

        string freeConstructor = Segment(
            code,
            "public MainWindow(LocalApplicationContext context)",
            "private void MoveToolsPanelIntoActionPanel");
        Assert.DoesNotContain("LoadDeviceIdAsync", freeConstructor);
        Assert.DoesNotContain("ApiAuthService", freeConstructor);
        Assert.DoesNotContain("ApiSession", freeConstructor);
        Assert.DoesNotContain("LicenseObservation", freeConstructor);
        Assert.DoesNotContain("ApplicationStartupController", freeConstructor);

        string connectService = Segment(code, "private async void ConnectService_Click", "private void ShowToolError");
        Assert.Contains("new StartupWindow { Owner = this }", connectService);
        Assert.Contains("ActivateServiceAsync(context)", connectService);
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
            if (File.Exists(candidate))
                return candidate;
            path = Directory.GetParent(path)?.FullName ?? path;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }

    private sealed class UnavailableUpdateClient : IPublicHonestFlowUpdateClient
    {
        public Task<SelfUpdateInfo?> GetCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromException<SelfUpdateInfo?>(new HttpRequestException("offline"));

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class SilentDialogs : IUserDialogService
    {
        public void ShowInformation(string message, string title) { }
        public void ShowWarning(string message, string title) { }
        public void ShowError(string message, string title) { }
        public bool Confirm(string message, string title, UserDialogIcon icon = UserDialogIcon.Warning) => false;
    }
}

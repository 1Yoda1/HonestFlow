using System;
using System.Collections.Generic;
using System.IO;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.ServiceConnection;
using HonestFlow.Models.Licensing;
using Newtonsoft.Json;
using Xunit;

namespace HonestFlow.Tests;

public sealed class ServiceConnectionTests
{
    [Fact]
    public void AllowedGrantWithExplicitService_ActivatesService()
    {
        Assert.Equal(ServiceConnectionState.Active, ServiceEntitlementEvaluator.Evaluate(Allowed(LicenseFeature.Service)));
    }

    [Theory]
    [InlineData(LicenseFeature.ViewAndRepair)]
    [InlineData(LicenseFeature.InstallAndMaintenance)]
    public void LegacyFeature_DoesNotActivateService(LicenseFeature feature)
    {
        Assert.Equal(ServiceConnectionState.NotEntitled, ServiceEntitlementEvaluator.Evaluate(Allowed(feature)));
    }

    [Fact]
    public void AllowedGrantWithoutService_DoesNotActivateService()
    {
        Assert.Equal(ServiceConnectionState.NotEntitled, ServiceEntitlementEvaluator.Evaluate(Allowed()));
    }

    [Theory]
    [InlineData(LicenseDecision.ManifestExpired, ServiceConnectionState.Expired)]
    [InlineData(LicenseDecision.OfflineGraceExpired, ServiceConnectionState.Expired)]
    [InlineData(LicenseDecision.ClientDisabled, ServiceConnectionState.Disabled)]
    [InlineData(LicenseDecision.DeviceDisabled, ServiceConnectionState.Disabled)]
    [InlineData(LicenseDecision.VersionTooOld, ServiceConnectionState.UpdateRequired)]
    [InlineData(LicenseDecision.LicenseNotIssued, ServiceConnectionState.NotEntitled)]
    [InlineData(LicenseDecision.DeviceNotRegistered, ServiceConnectionState.RegistrationRequired)]
    [InlineData(LicenseDecision.InvalidLicenseState, ServiceConnectionState.Unavailable)]
    public void DenialMapsToServiceState(LicenseDecision decision, ServiceConnectionState expected)
    {
        Assert.Equal(expected, ServiceEntitlementEvaluator.Evaluate(new LicenseObservationSnapshot
        {
            ClientId = "client-1",
            DeviceId = "device-1",
            Decision = decision
        }));
    }

    [Fact]
    public void LegacyGrantJsonStillDeserializesWithoutService()
    {
        const string json = "{\"features\":[\"ViewAndRepair\",\"InstallAndMaintenance\"]}";

        LicenseGrant grant = JsonConvert.DeserializeObject<LicenseGrant>(json);

        Assert.Equal(2, grant.Features.Count);
        Assert.DoesNotContain(LicenseFeature.Service, grant.Features);
    }

    [Fact]
    public void ServiceWindowReturnsContextAndNeverReplacesMainWindow()
    {
        string code = File.ReadAllText(ProjectFile("UI", "StartupWindow.xaml.cs"));
        string completion = Segment(code, "private async Task CompleteServiceConnectionAsync", "private void SetConnectionState");

        Assert.Contains("new ServiceRuntimeContextBuilder().BuildAsync(", completion);
        Assert.Contains("DialogResult = true", completion);
        Assert.DoesNotContain("new MainWindow", completion);
        Assert.DoesNotContain("new CompactMainWindow", completion);
        Assert.DoesNotContain("Application.Current.MainWindow", completion);
        Assert.DoesNotContain("new CompactMainWindow", code);
        Assert.DoesNotContain("Application.Current.MainWindow", code);

        string builder = File.ReadAllText(ProjectFile(
            "Application", "ServiceConnection", "ServiceRuntimeContextBuilder.cs"));
        Assert.Contains("new ServiceRuntimeContext(", builder);
    }

    [Fact]
    public void MainWindowPromotesAndDeactivatesInPlaceWithServiceExecutionEnabledOnlyWhileContextIsActive()
    {
        string code = File.ReadAllText(ProjectFile("UI", "MainWindow.xaml.cs"));
        string activation = Segment(code, "internal async Task ActivateServiceAsync", "private async Task DeactivateServiceAsync");
        string deactivation = Segment(code, "private async Task DeactivateServiceAsync", "private void ShowToolError");

        Assert.Contains("_applicationMode = ApplicationMode.Service", activation);
        Assert.Contains("_paidExecutionEnabled = true", activation);
        Assert.Contains("_autoFixWorkflow = CreateAutoFixWorkflow();", activation);
        Assert.Contains("RefreshTopologyAsync", activation);
        Assert.DoesNotContain("new MainWindow", activation);
        Assert.DoesNotContain("new CompactMainWindow", activation);

        Assert.Contains("_applicationMode = ApplicationMode.Free", deactivation);
        Assert.Contains("_pointStatusRefresh = _localContext?.PointStatusRefresh", deactivation);
        Assert.Contains("RefreshTopologyAsync", deactivation);
        Assert.DoesNotContain("Close()", deactivation);
    }

    [Fact]
    public void ServiceActivationUnlocksServiceActionsButFreeKeepsThemLocked()
    {
        string code = File.ReadAllText(ProjectFile("UI", "MainWindow.xaml.cs"));
        string freePresentation = Segment(code, "private void ApplyFreePresentation", "private void ApplyServicePresentation");
        string presentation = Segment(code, "private void ApplyServicePresentation", "private void ShowServiceConnectionStatus");

        foreach (string control in new[]
        {
            "SimpleFixButton", "DetailedFixButton", "ServiceControlColumn"
        })
        {
            Assert.Contains($"{control}.Visibility = Visibility.Visible", presentation);
            Assert.Contains($"{control}.Visibility = Visibility.Collapsed", freePresentation);
        }

        Assert.Contains("new ServiceOperationGuard(() => _serviceRuntime)", code);
        Assert.Contains("if (!_paidExecutionEnabled && send)", code);
        Assert.Contains("if (!_paidExecutionEnabled || _startup == null || _client == null) return;", code);
        Assert.Contains("if (!_paidExecutionEnabled) return;", code);
    }

    [Fact]
    public void ServiceRuntimeContextContainsValidatedClientRuntimeButNoTokens()
    {
        string context = File.ReadAllText(ProjectFile("Application", "ServiceConnection", "ServiceRuntimeContext.cs"));

        Assert.Contains("ApplicationStartupSession Session", context);
        Assert.Contains("IPData Client", context);
        Assert.Contains("LicenseObservationSnapshot Entitlement", context);
        Assert.Contains("ApiConfigurationResponse Configuration", context);
        Assert.Contains("VersionsData? EffectiveVersions", context);
        Assert.DoesNotContain("AccessToken", context);
        Assert.DoesNotContain("RefreshToken", context);
    }

    [Fact]
    public void RegistrationStatesRemainNonFatalAndUserInitiatedResumeIsRetained()
    {
        string startup = File.ReadAllText(ProjectFile("UI", "StartupWindow.xaml.cs"));
        string app = File.ReadAllText(ProjectFile("App.xaml.cs"));

        Assert.Contains("DeviceRegistrationStartupState.Pending => ServiceConnectionState.RegistrationPending", startup);
        Assert.Contains("DeviceRegistrationStartupState.Rejected => ServiceConnectionState.RegistrationRejected", startup);
        Assert.Contains("TryResumeRegistrationContinuationAsync", startup);
        Assert.DoesNotContain("new StartupWindow", app);
    }

    private static LicenseObservationSnapshot Allowed(params LicenseFeature[] features) => new()
    {
        ClientId = "client-1",
        DeviceId = "device-1",
        Decision = LicenseDecision.Allowed,
        Features = features
    };

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
}

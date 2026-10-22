using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Core;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Installation.Planning;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.Lm;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Models;
using HonestFlow.Models.Licensing;
using Xunit;

namespace HonestFlow.Tests;

public sealed class InstallationServiceHardeningTests
{
    [Fact]
    public async Task DeclinedOrganization_PropagatesCancellationBeforeDownloadOrMutation()
    {
        var packages = new StubPackages();
        var dialogs = new StubDialogs { Confirmed = false };
        var validation = new StubLmValidation
        {
            Result = new LmValidationResult { ApiStatus = new LmStatus { Inn = "other-organization" } }
        };
        var service = new ProbeInstallationService(packages, dialogs, new RecordingGuard(), validation);

        OperationCanceledException error = await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.CheckLmAndInstall(new IPData { Inn = "current-organization" }));

        Assert.Equal(CancellationToken.None, error.CancellationToken);
        Assert.Equal(1, dialogs.ConfirmCalls);
        Assert.Equal(0, dialogs.ErrorCalls);
        Assert.Equal(0, packages.Calls);
        Assert.Empty(service.Installed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task EveryComponent_RechecksTheLiveOperationGuard(bool reinstall, bool revokeAfterFirst)
    {
        var guard = new RecordingGuard();
        var service = new ProbeInstallationService(new StubPackages(), new StubDialogs(), guard, new StubLmValidation());
        if (revokeAfterFirst) service.AfterInstall = () => guard.Allowed = false;
        var client = new IPData { Architecture = "x64" };

        bool result = reinstall
            ? await service.ReinstallSelectedComponents(client, new[] { InstallationComponent.AtolDriver, InstallationComponent.Esm })
            : await service.CheckLmAndInstall(client, new InstallationOptions { SkipLmStack = true });

        Assert.Equal(!revokeAfterFirst, result);
        Assert.Equal(revokeAfterFirst ? 1 : 2, service.Installed.Count);
        Assert.Equal(InstallationComponent.AtolDriver, service.Installed[0]);
        Assert.Equal(4, guard.Operations.Count); // Entry, after download, then each component.
        Assert.All(guard.Operations, operation => Assert.Equal(
            reinstall ? LicenseOperation.ReinstallComponents : LicenseOperation.InstallComponents, operation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBetweenComponents_StopsBeforeNextMutation(bool reinstall)
    {
        using var cancellation = new CancellationTokenSource();
        var service = new ProbeInstallationService(
            new StubPackages(), new StubDialogs(), new RecordingGuard(), new StubLmValidation())
        {
            AfterInstall = cancellation.Cancel
        };
        var client = new IPData { Architecture = "x64" };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reinstall
            ? service.ReinstallSelectedComponents(client,
                new[] { InstallationComponent.AtolDriver, InstallationComponent.Esm }, cancellation.Token)
            : service.CheckLmAndInstall(client, new InstallationOptions { SkipLmStack = true }, cancellation.Token));

        Assert.Single(service.Installed);
    }

    [Fact]
    public async Task TargetedAtolUpdate_DoesNotQueryLmOrInstallOtherComponents()
    {
        var validation = new StubLmValidation();
        var service = new ProbeInstallationService(
            new StubPackages(), new StubDialogs(), new RecordingGuard(), validation);

        bool result = await service.CheckLmAndInstall(
            new IPData { Architecture = "x64" },
            new InstallationOptions
            {
                RequestedComponents = new[] { InstallationComponent.AtolDriver }
            });

        Assert.True(result);
        Assert.Equal(0, validation.CheckCalls);
        Assert.Equal(new[] { InstallationComponent.AtolDriver }, service.Installed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("current-configuration-token")]
    public void AuthenticatedComposition_UsesOnlyInjectedChzToken(string configuredToken)
    {
        var service = new ProbeInstallationService(
            new StubPackages(), new StubDialogs(), new RecordingGuard(), new StubLmValidation(), configuredToken);

        string token = service.ResolveToken(new IPData { Token = "stale-legacy-token" });

        Assert.Equal(configuredToken, token);
        Assert.NotEqual("stale-legacy-token", token);
    }

    [Fact]
    public void LegacyCompositionWithoutPackageSource_PreservesLegacyToken()
    {
        var service = new ProbeInstallationService(
            null, new StubDialogs(), new RecordingGuard(), new StubLmValidation());

        Assert.Equal("legacy-token", service.ResolveToken(new IPData { Token = "legacy-token" }));
    }

    private sealed class ProbeInstallationService : InstallationService
    {
        public ProbeInstallationService(IInstallationPackageSource packages, StubDialogs dialogs,
            RecordingGuard guard, StubLmValidation validation, string chzToken = null)
            : base(new StubLog(), new StubProgress(), dialogs, guard,
                packageSource: packages, lmChzToken: chzToken,
                lmValidator: validation, versionChecker: new StubVersions()) { }

        public List<InstallationComponent> Installed { get; } = new();
        public Action AfterInstall { get; set; }
        public string ResolveToken(IPData client) => ResolveLmChzToken(client);
        protected override Task<bool> InstallComponent(ComponentPlanItem item, IPData client, VersionsData versions,
            int progressStart, int progressEnd, CancellationToken cancellationToken)
        {
            Installed.Add(item.Component);
            AfterInstall?.Invoke();
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingGuard : ILicenseOperationGuard
    {
        public bool Allowed { get; set; } = true;
        public List<LicenseOperation> Operations { get; } = new();
        public void Demand(LicenseOperation operation)
        {
            Operations.Add(operation);
            if (!Allowed) new DenyLicenseOperationGuard().Demand(operation);
        }
    }

    private sealed class StubPackages : IInstallationPackageSource
    {
        public VersionsData Versions { get; } = new()
        {
            LmModule = "2.6.0", AtolDriver = "10.10.8.24", ESM = "1.6.3.2", Controller = "1.6.3.2"
        };
        public int Calls { get; private set; }
        public Task<string> ResolveInstallerAsync(InstallationComponent component, IProgress<int> progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls++;
            // Only satisfy the real orchestration's existence check; installers are overridden.
            return Task.FromResult(typeof(InstallationServiceHardeningTests).Assembly.Location);
        }
    }

    private sealed class StubLmValidation : ILmValidationService
    {
        public LmValidationResult Result { get; init; } = new();
        public int CheckCalls { get; private set; }
        public Task<LmValidationResult> CheckLmStatus(string version)
        {
            CheckCalls++;
            return Task.FromResult(Result);
        }
        public Task<LmStatus> GetLmStatus(string version) => Task.FromResult(Result.ApiStatus);
        public Task<(bool NeedInstall, string DisplayStatus)> GetLmStatusInfo(string version) =>
            Task.FromResult((Result.NeedsInstall, Result.DisplayStatus));
    }

    private sealed class StubVersions : IVersionCheckService
    {
        public bool NeedAtolInstall(IPData client, string version) => true;
        public bool NeedEsmInstall(string version) => true;
        public bool NeedControllerInstall(string version) => false;
        public string GetAtolDriverInfo() => null;
        public string GetAtolDriverInfo(string architecture) => null;
        public string GetEsmVersion() => null;
        public string GetControllerVersion() => null;
    }

    private sealed class StubLog : ILogService
    {
        public void LogUser(string message, bool isError = false) { }
        public void LogDebug(string message) { }
        public string GetUserLog() => string.Empty;
    }

    private sealed class StubProgress : IProgressService
    {
        public void SetProgress(int percent, string stepName) { }
    }

    private sealed class StubDialogs : IUserDialogService
    {
        public bool Confirmed { get; init; } = true;
        public int ConfirmCalls { get; private set; }
        public int ErrorCalls { get; private set; }
        public void ShowInformation(string message, string title) { }
        public void ShowWarning(string message, string title) { }
        public void ShowError(string message, string title) => ErrorCalls++;
        public bool Confirm(string message, string title, UserDialogIcon icon = UserDialogIcon.Warning)
        {
            ConfirmCalls++;
            return Confirmed;
        }
    }
}

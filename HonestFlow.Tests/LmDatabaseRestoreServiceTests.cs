using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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

public sealed class LmDatabaseRestoreServiceTests
{
    private const string RawError = "raw internal failure detail";
    private static string ExistingFile => typeof(LmDatabaseRestoreServiceTests).Assembly.Location;
    private static IPData Client() => new() { Inn = "123456789012", HasLmDatabaseBackup = true };

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PreflightException_ReturnsSafePhaseResult(bool archivePhase, bool timeout)
    {
        var fixture = new Fixture();
        Exception error = timeout ? new TaskCanceledException(RawError) : new HttpRequestException(RawError);
        if (archivePhase)
            fixture.Service.ResolveArchive = _ => Task.FromException(error);
        else
            fixture.Packages.Resolve = _ => Task.FromException<string>(error);

        LmDatabaseRestoreResult result = await fixture.Service.RestoreWithResult(Client());

        Assert.Equal(archivePhase ? LmDatabaseRestoreStatus.DownloadFailed : LmDatabaseRestoreStatus.InstallerUnavailable,
            result.Status);
        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(RawError, result.UserMessage);
        Assert.DoesNotContain(RawError, fixture.Log.GetUserLog());
        Assert.All(fixture.Dialogs.Messages, message => Assert.DoesNotContain(RawError, message));
        Assert.Equal(0, fixture.Dialogs.ConfirmCalls);
        Assert.Equal(0, fixture.Service.RestoreCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationDuringPreflight_IsPropagated(bool archivePhase)
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture();
        if (archivePhase)
            fixture.Service.ResolveArchive = token =>
            {
                cancellation.Cancel();
                return Task.FromCanceled(token);
            };
        else
            fixture.Packages.Resolve = token =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<string>(token);
            };

        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.RestoreWithResult(Client(), cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, fixture.Service.RestoreCalls);
        Assert.Equal(0, fixture.Dialogs.ConfirmCalls);
        Assert.Equal(0, fixture.Dialogs.ErrorCalls);
    }

    [Fact]
    public async Task PrecancelledRequest_DoesNotResolveOrDemand()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var fixture = new Fixture();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.RestoreWithResult(Client(), cancellation.Token));

        Assert.Equal(0, fixture.Guard.Calls);
        Assert.Equal(0, fixture.Packages.Calls);
    }

    [Fact]
    public async Task UnexpectedPreflightFailure_ReturnsSafeFallback()
    {
        var fixture = new Fixture();
        fixture.Service.FindFolderError = new IOException(RawError);

        LmDatabaseRestoreResult result = await fixture.Service.RestoreWithResult(Client());

        Assert.Equal(LmDatabaseRestoreStatus.Failed, result.Status);
        Assert.DoesNotContain(RawError, result.UserMessage);
        Assert.Equal(0, fixture.Packages.Calls);
        Assert.Equal(0, fixture.Service.RestoreCalls);
    }

    [Fact]
    public async Task UnavailableInstaller_PreservesKnownResult()
    {
        var fixture = new Fixture();
        fixture.Packages.Resolve = _ => Task.FromResult<string>(null);

        LmDatabaseRestoreResult result = await fixture.Service.RestoreWithResult(Client());

        Assert.Equal(LmDatabaseRestoreStatus.InstallerUnavailable, result.Status);
        Assert.Equal(0, fixture.Service.RestoreCalls);
    }

    [Fact]
    public async Task AmbiguousDownloadFalse_PreservesDownloadFailed()
    {
        var fixture = new Fixture();
        fixture.Service.DownloadFailed = true;

        LmDatabaseRestoreResult result = await fixture.Service.RestoreWithResult(Client());

        Assert.Equal(LmDatabaseRestoreStatus.DownloadFailed, result.Status);
        Assert.Equal(0, fixture.Service.RestoreCalls);
        Assert.Equal(0, fixture.Dialogs.ConfirmCalls);
    }

    [Fact]
    public async Task DeniedBeforePreflight_DoesNotResolve()
    {
        var fixture = new Fixture();
        fixture.Guard.Allowed = false;

        LmDatabaseRestoreResult result = await fixture.Service.RestoreWithResult(Client());

        Assert.Equal(LmDatabaseRestoreStatus.Denied, result.Status);
        Assert.Equal(0, fixture.Packages.Calls);
        Assert.Equal(0, fixture.Service.RestoreCalls);
    }

    [Fact]
    public async Task RevokedWhileConfirming_DoesNotRestore()
    {
        var fixture = new Fixture();
        fixture.Dialogs.OnConfirm = () => fixture.Guard.Allowed = false;

        LmDatabaseRestoreResult result = await fixture.Service.RestoreWithResult(Client());

        Assert.Equal(LmDatabaseRestoreStatus.Denied, result.Status);
        Assert.Equal(2, fixture.Guard.Calls);
        Assert.Equal(0, fixture.Service.RestoreCalls);
    }

    [Fact]
    public async Task DeclinedConfirmation_ReturnsCancelled()
    {
        var fixture = new Fixture();
        fixture.Dialogs.Confirmed = false;

        LmDatabaseRestoreResult result = await fixture.Service.RestoreWithResult(Client());

        Assert.Equal(LmDatabaseRestoreStatus.Cancelled, result.Status);
        Assert.Equal(0, fixture.Service.RestoreCalls);
    }

    [Fact]
    public async Task InstallerDeclinesDatabaseDeletion_ReturnsCancelledWithoutError()
    {
        var fixture = new Fixture();
        fixture.Service.RestoreError = new OperationCanceledException(RawError);

        LmDatabaseRestoreResult result = await fixture.Service.RestoreWithResult(Client());

        Assert.Equal(LmDatabaseRestoreStatus.Cancelled, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal(0, fixture.Dialogs.ErrorCalls);
        Assert.DoesNotContain(RawError, result.UserMessage);
    }

    [Fact]
    public async Task CompletedRestore_ReturnsSuccessAfterSecondGuard()
    {
        var fixture = new Fixture();

        LmDatabaseRestoreResult result = await fixture.Service.RestoreWithResult(Client());

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fixture.Service.RestoreCalls);
        Assert.Equal(2, fixture.Guard.Calls);
    }

    [Fact]
    public async Task BooleanCompatibilityWrapper_ReturnsFalseForPreflightFailure()
    {
        var fixture = new Fixture();
        fixture.Packages.Resolve = _ => Task.FromException<string>(new IOException(RawError));

        Assert.False(await fixture.Service.Restore(Client()));
    }

    private sealed class Fixture
    {
        public StubLog Log { get; } = new();
        public StubDialogs Dialogs { get; } = new();
        public RevocableGuard Guard { get; } = new();
        public StubPackages Packages { get; } = new();
        public StubService Service { get; }
        public Fixture() => Service = new StubService(Log, Dialogs, Guard, Packages);
    }

    // Override only machine/network boundaries; exercise the real restore orchestration.
    private sealed class StubService : LmDatabaseRestoreService
    {
        public StubService(StubLog log, StubDialogs dialogs, RevocableGuard guard, StubPackages packages)
            : base(log, new StubProgress(), dialogs, guard, false, packages) { }
        public Func<CancellationToken, Task> ResolveArchive { get; set; } = _ => Task.CompletedTask;
        public Exception FindFolderError { get; set; }
        public Exception RestoreError { get; set; }
        public bool DownloadFailed { get; set; }
        public int RestoreCalls { get; private set; }
        protected override string FindInstalledRegimeFolder()
        {
            if (FindFolderError != null) throw FindFolderError;
            return @"C:\Regime";
        }
        protected override async Task<DatabaseArchiveResolution> ResolveDatabaseArchivePath(
            string archiveName, CancellationToken cancellationToken)
        {
            await ResolveArchive(cancellationToken);
            return DownloadFailed ? DatabaseArchiveResolution.DownloadFailure() : DatabaseArchiveResolution.Found(ExistingFile);
        }
        protected override Task RestoreDatabaseFromArchive(
            string installerPath, string archivePath, string installFolder, CancellationToken cancellationToken)
        {
            RestoreCalls++;
            return RestoreError == null ? Task.CompletedTask : Task.FromException(RestoreError);
        }
    }

    private sealed class StubPackages : IInstallationPackageSource
    {
        public VersionsData Versions { get; } = new() { LmModule = "2.6.0" };
        public Func<CancellationToken, Task<string>> Resolve { get; set; } = _ => Task.FromResult(ExistingFile);
        public int Calls { get; private set; }
        public Task<string> ResolveInstallerAsync(InstallationComponent component, IProgress<int> progress, CancellationToken token)
        {
            Calls++;
            return Resolve(token);
        }
    }

    private sealed class RevocableGuard : ILicenseOperationGuard
    {
        public bool Allowed { get; set; } = true;
        public int Calls { get; private set; }
        public void Demand(LicenseOperation operation)
        {
            Assert.Equal(LicenseOperation.RestoreLmDatabase, operation);
            Calls++;
            if (!Allowed) new DenyLicenseOperationGuard().Demand(operation);
        }
    }

    private sealed class StubProgress : IProgressService
    {
        public void SetProgress(int percent, string stepName) { }
    }

    private sealed class StubLog : ILogService
    {
        private readonly List<string> _messages = new();
        public void LogUser(string message, bool isError = false) => _messages.Add(message);
        public void LogDebug(string message) { }
        public string GetUserLog() => string.Join("\n", _messages);
    }

    private sealed class StubDialogs : IUserDialogService
    {
        public List<string> Messages { get; } = new();
        public Action OnConfirm { get; set; }
        public bool Confirmed { get; set; } = true;
        public int ConfirmCalls { get; private set; }
        public int ErrorCalls { get; private set; }
        public void ShowInformation(string message, string title) => Messages.Add(message);
        public void ShowWarning(string message, string title) => Messages.Add(message);
        public void ShowError(string message, string title) { ErrorCalls++; Messages.Add(message); }
        public bool Confirm(string message, string title, UserDialogIcon icon = UserDialogIcon.Warning)
        {
            ConfirmCalls++;
            OnConfirm?.Invoke();
            return Confirmed;
        }
    }
}

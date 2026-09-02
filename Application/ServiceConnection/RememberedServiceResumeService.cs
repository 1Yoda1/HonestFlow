using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Bootstrap;
using HonestFlow.Application.Core;
using HonestFlow.Application.DeviceIdentity;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.DeviceIdentity;
using HonestFlow.Infrastructure.Dialogs;

namespace HonestFlow.Application.ServiceConnection;

public sealed class RememberedServiceResumeCandidate
{
    public RememberedServiceResumeCandidate(string clientId, string deviceId)
    {
        ClientId = clientId;
        DeviceId = deviceId;
    }

    public string ClientId { get; }
    public string DeviceId { get; }
}

public interface IRememberedServiceResumeProbe
{
    Task<RememberedServiceResumeCandidate?> TryLoadAsync(CancellationToken cancellationToken);
}

public interface IRememberedServiceResumePipeline
{
    Task<ServiceConnectionResult> ResumeAsync(
        RememberedServiceResumeCandidate candidate,
        CancellationToken cancellationToken);
}

public sealed class FileRememberedServiceResumeProbe : IRememberedServiceResumeProbe
{
    private readonly IApiSessionStore _sessionStore;
    private readonly IExistingDeviceIdentityService _deviceIdentity;

    public FileRememberedServiceResumeProbe(
        IApiSessionStore sessionStore,
        IExistingDeviceIdentityService deviceIdentity)
    {
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _deviceIdentity = deviceIdentity ?? throw new ArgumentNullException(nameof(deviceIdentity));
    }

    public async Task<RememberedServiceResumeCandidate?> TryLoadAsync(
        CancellationToken cancellationToken)
    {
        ApiSession session = await _sessionStore.LoadAsync(cancellationToken);
        if (session is null ||
            !session.RememberActiveSession ||
            session.LicensePolicyEnabled == false ||
            string.IsNullOrWhiteSpace(session.RefreshToken) ||
            string.IsNullOrWhiteSpace(session.ClientId) ||
            string.IsNullOrWhiteSpace(session.ExternalDeviceId))
        {
            return null;
        }

        DeviceIdentityResult identity = await _deviceIdentity.TryLoadExistingAsync(cancellationToken);
        if (!identity.IsAvailable ||
            !string.Equals(identity.DeviceId, session.ExternalDeviceId, StringComparison.Ordinal))
        {
            return null;
        }

        return new RememberedServiceResumeCandidate(session.ClientId, identity.DeviceId);
    }
}

public sealed class RememberedServiceResumeService
{
    private readonly IRememberedServiceResumeProbe _probe;
    private readonly IRememberedServiceResumePipeline _pipeline;
    private int _started;

    public RememberedServiceResumeService(
        IRememberedServiceResumeProbe probe,
        IRememberedServiceResumePipeline pipeline)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public static RememberedServiceResumeService CreateProduction()
    {
        var deviceIdentity = new FileDeviceIdentityService(
            new DpapiDeviceIdentityStateProtector());
        return new RememberedServiceResumeService(
            new FileRememberedServiceResumeProbe(
                new FileApiSessionStore(),
                deviceIdentity),
            new RememberedServiceResumePipeline());
    }

    public async Task<ServiceConnectionResult> TryResumeAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return new ServiceConnectionResult(
                ServiceConnectionState.Disconnected,
                ServiceEntitlementEvaluator.Message(ServiceConnectionState.Disconnected));
        }

        try
        {
            RememberedServiceResumeCandidate? candidate =
                await _probe.TryLoadAsync(cancellationToken);
            if (candidate is null)
            {
                return new ServiceConnectionResult(
                    ServiceConnectionState.Disconnected,
                    ServiceEntitlementEvaluator.Message(ServiceConnectionState.Disconnected));
            }

            ServiceConnectionResult result = await _pipeline.ResumeAsync(candidate, cancellationToken);
            if (result.Context is not null &&
                (!string.Equals(result.Context.Client.ClientId, candidate.ClientId, StringComparison.Ordinal) ||
                 !string.Equals(result.Context.DeviceId, candidate.DeviceId, StringComparison.Ordinal)))
            {
                Logger.Warning(
                    "Event=RememberedServiceResumeRejected Reason=PersistedIdentityMismatch",
                    nameof(RememberedServiceResumeService));
                return new ServiceConnectionResult(
                    ServiceConnectionState.Unavailable,
                    ServiceEntitlementEvaluator.Message(ServiceConnectionState.Unavailable));
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Silent remembered Service resume failed", nameof(RememberedServiceResumeService));
            return new ServiceConnectionResult(
                ServiceConnectionState.Unavailable,
                ServiceEntitlementEvaluator.Message(ServiceConnectionState.Unavailable));
        }
    }
}

public sealed class RememberedServiceResumePipeline : IRememberedServiceResumePipeline
{
    private readonly ServiceRuntimeContextBuilder _contextBuilder;

    public RememberedServiceResumePipeline(ServiceRuntimeContextBuilder? contextBuilder = null)
    {
        _contextBuilder = contextBuilder ?? new ServiceRuntimeContextBuilder();
    }

    public async Task<ServiceConnectionResult> ResumeAsync(
        RememberedServiceResumeCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var controller = new ApplicationStartupController();
        ApplicationStartupSession session = await controller.InitializeAsync(
            SilentProgress.Instance,
            SilentDialogs.Instance,
            cancellationToken);
        LicenseAuthenticationResult authentication = await controller.TryResumeAsync(
            session,
            progress: null,
            cancellationToken);

        return await _contextBuilder.BuildAsync(
            controller,
            session,
            authentication.Client,
            authentication.LicenseSnapshot,
            cancellationToken);
    }

    private sealed class SilentProgress : IProgressService
    {
        public static SilentProgress Instance { get; } = new();
        public void SetProgress(int percent, string stepName) { }
    }

    private sealed class SilentDialogs : IUserDialogService
    {
        public static SilentDialogs Instance { get; } = new();
        public void ShowInformation(string message, string title) { }
        public void ShowWarning(string message, string title) { }
        public void ShowError(string message, string title) { }
        public bool Confirm(string message, string title, UserDialogIcon icon = UserDialogIcon.Warning) => false;
    }
}

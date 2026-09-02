using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.DeviceIdentity;
using HonestFlow.Application.ServiceConnection;
using HonestFlow.Infrastructure.Api;
using Xunit;

namespace HonestFlow.Tests;

public sealed class RememberedServiceResumeTests
{
    [Fact]
    public async Task NoRememberedSession_DoesNotReadIdentityOrStartServicePipeline()
    {
        var identity = new ExistingIdentity(DeviceIdentityResult.Available(
            DeviceIdentityStatus.Existing, "device-1"));
        var pipeline = new RecordingPipeline();
        var service = Service(new MemoryStore(null), identity, pipeline);

        ServiceConnectionResult result = await service.TryResumeAsync(CancellationToken.None);

        Assert.Equal(ServiceConnectionState.Disconnected, result.State);
        Assert.Equal(0, identity.LoadCount);
        Assert.Equal(0, pipeline.CallCount);
    }

    [Fact]
    public async Task RememberedSessionWithoutIdentity_DoesNotCreateOrStartPipeline()
    {
        var identity = new ExistingIdentity(DeviceIdentityResult.Unavailable("missing"));
        var pipeline = new RecordingPipeline();
        var service = Service(new MemoryStore(ActiveSession()), identity, pipeline);

        ServiceConnectionResult result = await service.TryResumeAsync(CancellationToken.None);

        Assert.Equal(ServiceConnectionState.Disconnected, result.State);
        Assert.Equal(1, identity.LoadCount);
        Assert.Equal(0, pipeline.CallCount);
    }

    [Fact]
    public async Task RegistrationContinuationStore_IsNotAnAutomaticResumeSource()
    {
        var rememberedStore = new MemoryStore(null);
        var registrationContinuationStore = new MemoryStore(ActiveSession());
        var identity = new ExistingIdentity(DeviceIdentityResult.Available(
            DeviceIdentityStatus.Existing, "device-1"));
        var pipeline = new RecordingPipeline();

        var service = Service(rememberedStore, identity, pipeline);
        ServiceConnectionResult result = await service.TryResumeAsync(CancellationToken.None);

        Assert.NotNull(registrationContinuationStore.Session);
        Assert.Equal(ServiceConnectionState.Disconnected, result.State);
        Assert.Equal(0, pipeline.CallCount);
    }

    [Fact]
    public async Task RememberedActiveSessionAndMatchingIdentity_StartPipeline()
    {
        var pipeline = new RecordingPipeline(new ServiceConnectionResult(
            ServiceConnectionState.NotEntitled,
            "not entitled"));
        var service = Service(
            new MemoryStore(ActiveSession()),
            new ExistingIdentity(DeviceIdentityResult.Available(
                DeviceIdentityStatus.Existing, "device-1")),
            pipeline);

        ServiceConnectionResult result = await service.TryResumeAsync(CancellationToken.None);

        Assert.Equal(ServiceConnectionState.NotEntitled, result.State);
        Assert.Equal(1, pipeline.CallCount);
        Assert.Equal("client-1", pipeline.LastCandidate.ClientId);
        Assert.Equal("device-1", pipeline.LastCandidate.DeviceId);
    }

    [Fact]
    public async Task RememberedSessionForDifferentDevice_DoesNotStartPipeline()
    {
        var pipeline = new RecordingPipeline();
        var service = Service(
            new MemoryStore(ActiveSession()),
            new ExistingIdentity(DeviceIdentityResult.Available(
                DeviceIdentityStatus.Existing, "device-2")),
            pipeline);

        ServiceConnectionResult result = await service.TryResumeAsync(CancellationToken.None);

        Assert.Equal(ServiceConnectionState.Disconnected, result.State);
        Assert.Equal(0, pipeline.CallCount);
    }

    [Fact]
    public async Task ConcurrentOrRepeatedResume_DoesNotDuplicatePipeline()
    {
        var pipeline = new BlockingPipeline();
        var service = Service(
            new MemoryStore(ActiveSession()),
            new ExistingIdentity(DeviceIdentityResult.Available(
                DeviceIdentityStatus.Existing, "device-1")),
            pipeline);

        Task<ServiceConnectionResult> first = service.TryResumeAsync(CancellationToken.None);
        await pipeline.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        ServiceConnectionResult repeated = await service.TryResumeAsync(CancellationToken.None);
        pipeline.Complete();
        await first;

        Assert.Equal(ServiceConnectionState.Disconnected, repeated.State);
        Assert.Equal(1, pipeline.CallCount);
    }

    [Fact]
    public async Task CancellationStopsBackgroundResume()
    {
        var pipeline = new BlockingPipeline();
        var service = Service(
            new MemoryStore(ActiveSession()),
            new ExistingIdentity(DeviceIdentityResult.Available(
                DeviceIdentityStatus.Existing, "device-1")),
            pipeline);
        using var cancellation = new CancellationTokenSource();

        Task<ServiceConnectionResult> resume = service.TryResumeAsync(cancellation.Token);
        await pipeline.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resume);
    }

    [Fact]
    public async Task LogoutClearsRememberedSessionSoNextStartupDoesNotResume()
    {
        var store = new MemoryStore(ActiveSession());
        using var http = new HttpClient(new NoContentHandler())
        {
            BaseAddress = new Uri("https://example.test/")
        };
        var apiSession = new ApiSessionService(http, store);

        await apiSession.LogoutAsync(CancellationToken.None);

        var pipeline = new RecordingPipeline();
        ServiceConnectionResult result = await Service(
            store,
            new ExistingIdentity(DeviceIdentityResult.Available(
                DeviceIdentityStatus.Existing, "device-1")),
            pipeline).TryResumeAsync(CancellationToken.None);
        Assert.Equal(ServiceConnectionState.Disconnected, result.State);
        Assert.Equal(0, pipeline.CallCount);
    }

    [Fact]
    public void MainWindowUsesOneGateAndManualConnectionCancelsSilentResume()
    {
        string code = File.ReadAllText(ProjectFile("UI", "MainWindow.xaml.cs"));

        Assert.Contains("_serviceConnectionGate", code);
        Assert.Contains("_silentServiceResumeCancellation?.Cancel();", code);
        Assert.Contains("DispatcherPriority.ContextIdle", code);
        Assert.Contains("RememberedServiceResumeService", code);
        Assert.Contains("await ActivateServiceAsync(context);", code);
        Assert.DoesNotContain("new MainWindow", Segment(
            code,
            "private async Task ResumeRememberedServiceAsync",
            "internal async Task ActivateServiceAsync"));
    }

    private static RememberedServiceResumeService Service(
        IApiSessionStore store,
        IExistingDeviceIdentityService identity,
        IRememberedServiceResumePipeline pipeline) =>
        new(new FileRememberedServiceResumeProbe(store, identity), pipeline);

    private static ApiSession ActiveSession() => new()
    {
        AccessToken = "access",
        RefreshToken = "refresh",
        AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10),
        ClientId = "client-1",
        ClientName = "Client 1",
        ExternalDeviceId = "device-1",
        RememberActiveSession = true,
        LicensePolicyEnabled = true
    };

    private sealed class MemoryStore : IApiSessionStore
    {
        public MemoryStore(ApiSession session) => Session = session;
        public ApiSession Session { get; private set; }
        public Task<ApiSession> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Session);
        public Task SaveAsync(ApiSession session, CancellationToken cancellationToken)
        {
            Session = session;
            return Task.CompletedTask;
        }
        public Task ClearAsync(CancellationToken cancellationToken)
        {
            Session = null;
            return Task.CompletedTask;
        }
    }

    private sealed class ExistingIdentity : IExistingDeviceIdentityService
    {
        private readonly DeviceIdentityResult _result;
        public ExistingIdentity(DeviceIdentityResult result) => _result = result;
        public int LoadCount { get; private set; }
        public Task<DeviceIdentityResult> TryLoadExistingAsync(CancellationToken cancellationToken)
        {
            LoadCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class RecordingPipeline : IRememberedServiceResumePipeline
    {
        private readonly ServiceConnectionResult _result;
        public RecordingPipeline(ServiceConnectionResult result = null) =>
            _result = result ?? new ServiceConnectionResult(
                ServiceConnectionState.Unavailable, "unavailable");
        public int CallCount { get; private set; }
        public RememberedServiceResumeCandidate LastCandidate { get; private set; }
        public Task<ServiceConnectionResult> ResumeAsync(
            RememberedServiceResumeCandidate candidate,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastCandidate = candidate;
            return Task.FromResult(_result);
        }
    }

    private sealed class BlockingPipeline : IRememberedServiceResumePipeline
    {
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }
        public async Task<ServiceConnectionResult> ResumeAsync(
            RememberedServiceResumeCandidate candidate,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Started.TrySetResult();
            await _completion.Task.WaitAsync(cancellationToken);
            return new ServiceConnectionResult(ServiceConnectionState.Unavailable, "unavailable");
        }
        public void Complete() => _completion.TrySetResult();
    }

    private sealed class NoContentHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
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
}

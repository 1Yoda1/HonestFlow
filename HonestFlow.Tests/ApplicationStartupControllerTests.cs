using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Bootstrap;
using HonestFlow.Application.Core;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Models;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class ApplicationStartupControllerTests
    {
        [Fact]
        public async Task Authenticate_ConfiguresApiSessionPersistenceBeforeLogin()
        {
            var apiSession = new CapturingApiSession();
            var auth = new CapturingApiAuth(apiSession);
            var startup = new StartupResult
            {
                UseRemoteConfigMode = true,
                AuthService = auth
            };
            var session = new ApplicationStartupSession(
                startup,
                new FakeLog(),
                new SellerAuthenticationWorkflow(auth, LicenseObservationSnapshotStore.Instance));

            LicenseAuthenticationResult result = await new ApplicationStartupController().AuthenticateAsync(
                session,
                string.Empty,
                "secret-password",
                false,
                null,
                CancellationToken.None);

            Assert.NotNull(result.Client);
            Assert.False(apiSession.PersistSession);
            Assert.True(auth.PersistenceWasConfiguredBeforeAuthentication);
        }

        [Fact]
        public async Task RegistrationContinuation_IsRestoredBeforeNormalStartupResume()
        {
            var apiSession = new CapturingApiSession
            {
                Continuation = new ApiSession
                {
                    ClientId = "client-1",
                    ClientName = "Point",
                    ExternalDeviceId = "device-1"
                }
            };
            var auth = new CapturingApiAuth(apiSession);
            var session = new ApplicationStartupSession(
                new StartupResult { UseRemoteConfigMode = true, AuthService = auth },
                new FakeLog(),
                new SellerAuthenticationWorkflow(auth, LicenseObservationSnapshotStore.Instance));

            LicenseObservationSnapshot continuation = await new ApplicationStartupController()
                .TryResumeRegistrationContinuationAsync(session, CancellationToken.None);

            Assert.NotNull(continuation);
            Assert.Equal(LicenseDecision.DeviceNotRegistered, continuation.Decision);
            Assert.Equal("client-1", continuation.ClientId);
            Assert.Equal("Point", continuation.ClientName);
            Assert.Equal("device-1", continuation.DeviceId);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(null)]
        public async Task RegistrationContinuation_EnabledOrLegacyPolicy_RemainsRegistrationState(bool? policyEnabled)
        {
            var apiSession = ContinuationSession(policyEnabled);
            LicenseObservationSnapshot state = await ResumeContinuationAsync(apiSession);

            Assert.Equal(LicenseDecision.DeviceNotRegistered, state.Decision);
            Assert.True(apiSession.RefreshCalled);
            Assert.False(apiSession.ContinuationCleared);
        }

        [Fact]
        public async Task RegistrationContinuation_ExplicitPolicyOff_BlocksAndClearsOnlyLocalContinuation()
        {
            var apiSession = ContinuationSession(false);
            LicenseObservationSnapshot state = await ResumeContinuationAsync(apiSession);

            Assert.Equal(LicenseDecision.ClientDisabled, state.Decision);
            Assert.Equal("CLIENT_ACCESS_DISABLED", state.TechnicalCode);
            Assert.True(apiSession.RefreshCalled);
            Assert.True(apiSession.ContinuationCleared);
        }

        private static CapturingApiSession ContinuationSession(bool? policyEnabled) => new()
        {
            Continuation = new ApiSession { ClientId = "client-1", ClientName = "Point", ExternalDeviceId = "device-1" },
            LicensePolicyEnabled = policyEnabled
        };

        private static async Task<LicenseObservationSnapshot> ResumeContinuationAsync(CapturingApiSession apiSession)
        {
            var auth = new CapturingApiAuth(apiSession);
            var session = new ApplicationStartupSession(
                new StartupResult { UseRemoteConfigMode = true, AuthService = auth },
                new FakeLog(), new SellerAuthenticationWorkflow(auth, LicenseObservationSnapshotStore.Instance));
            return await new ApplicationStartupController().TryResumeRegistrationContinuationAsync(session, CancellationToken.None);
        }

        private sealed class CapturingApiAuth : IApiCredentialAuthService, IApiSessionProvider
        {
            private readonly CapturingApiSession _session;

            public CapturingApiAuth(CapturingApiSession session) => _session = session;

            public bool PersistenceWasConfiguredBeforeAuthentication { get; private set; }
            public IApiSessionService ApiSessionService => _session;

            public void LoadIpList() { }
            public IPData Authenticate(string password) => null;

            public Task<LicenseAuthenticationResult> AuthenticateAsync(
                string login,
                string password,
                IProgress<LicenseAuthenticationProgress> progress,
                CancellationToken cancellationToken)
            {
                PersistenceWasConfiguredBeforeAuthentication = _session.WasConfigured;
                return Task.FromResult(new LicenseAuthenticationResult(
                    new IPData { ClientId = "client-1", Name = "Point" },
                    new LicenseObservationSnapshot { Decision = LicenseDecision.Allowed }));
            }

            public Task<LicenseAuthenticationResult> TryResumeAsync(
                IProgress<LicenseAuthenticationProgress> progress,
                CancellationToken cancellationToken) =>
                Task.FromResult(new LicenseAuthenticationResult(null, null));
        }

        private sealed class CapturingApiSession : IApiSessionService, IApiSessionPersistenceController,
            IApiSessionRefresher, IApiClientAccessStateProvider
        {
            public bool PersistSession { get; private set; } = true;
            public bool WasConfigured { get; private set; }
            public ApiSession Continuation { get; set; }
            public bool? LicensePolicyEnabled { get; set; }
            public string ClientId => Continuation?.ClientId;
            public string ClientName => Continuation?.ClientName;
            public bool RefreshCalled { get; private set; }
            public bool ContinuationCleared { get; private set; }

            public void SetPersistSession(bool persistSession)
            {
                PersistSession = persistSession;
                WasConfigured = true;
            }

            public Task<ApiSession> RestoreRegistrationContinuationAsync(CancellationToken cancellationToken) =>
                Task.FromResult(Continuation);
            public Task PersistRegistrationContinuationAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task ClearRegistrationContinuationAsync(CancellationToken cancellationToken)
            {
                ContinuationCleared = true;
                return Task.CompletedTask;
            }
            public void PrepareForRegistrationCompletion() { }
            public Task<bool> RefreshSessionAsync(CancellationToken cancellationToken)
            {
                RefreshCalled = true;
                return Task.FromResult(true);
            }

            public Task<ApiTokenResponse> LoginAsync(
                string login,
                string password,
                string deviceId,
                string deviceName,
                CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task<HttpResponseMessage> SendAuthorizedAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) => throw new NotSupportedException();
        }

        private sealed class FakeLog : ILogService
        {
            public void LogUser(string message, bool isError = false) { }
            public void LogDebug(string message) { }
            public string GetUserLog() => string.Empty;
        }
    }
}

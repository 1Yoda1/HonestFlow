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

        private sealed class CapturingApiSession : IApiSessionService, IApiSessionPersistenceController
        {
            public bool PersistSession { get; private set; } = true;
            public bool WasConfigured { get; private set; }

            public void SetPersistSession(bool persistSession)
            {
                PersistSession = persistSession;
                WasConfigured = true;
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

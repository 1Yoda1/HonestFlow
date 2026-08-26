using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Core;
using HonestFlow.Application.Installation;
using HonestFlow.Application.PointStatus;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class TsPiotRegistrationWorkflowTests
    {
        [Fact]
        public async Task RegisterAsync_PortUnavailable_DoesNotCallRegistrationClient()
        {
            var client = new StubRegistrationClient(TsPiotRegistrationResult.Success());
            var workflow = new TsPiotRegistrationWorkflow(
                client,
                new StubPortProbe(EsmApiPortProbeResult.Unavailable(null, "refused")),
                new StubLog());

            TsPiotRegistrationResult result = await workflow.RegisterAsync(CancellationToken.None);

            Assert.Equal(TsPiotRegistrationStatus.EsmUnavailable, result.Status);
            Assert.False(client.Called);
        }

        [Fact]
        public async Task RegisterAsync_ReadyPort_UsesRegistrationClient()
        {
            var client = new StubRegistrationClient(TsPiotRegistrationResult.Success());
            var workflow = new TsPiotRegistrationWorkflow(
                client,
                new StubPortProbe(EsmApiPortProbeResult.Available(51888)),
                new StubLog());

            TsPiotRegistrationResult result = await workflow.RegisterAsync(CancellationToken.None);

            Assert.Equal(TsPiotRegistrationStatus.Success, result.Status);
            Assert.True(client.Called);
        }

        [Fact]
        public async Task RegisterAsync_Cancellation_IsNotSwallowed()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var workflow = new TsPiotRegistrationWorkflow(
                new StubRegistrationClient(TsPiotRegistrationResult.Success()),
                new StubPortProbe(EsmApiPortProbeResult.Available(51888)),
                new StubLog());

            await Assert.ThrowsAsync<OperationCanceledException>(() => workflow.RegisterAsync(cancellation.Token));
        }

        private sealed class StubRegistrationClient : IEsmTsPiotRegistrationClient
        {
            private readonly TsPiotRegistrationResult _result;
            public StubRegistrationClient(TsPiotRegistrationResult result) => _result = result;
            public bool Called { get; private set; }
            public Task<TsPiotRegistrationResult> RegisterAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Called = true;
                return Task.FromResult(_result);
            }
        }

        private sealed class StubPortProbe : IEsmApiPortProbe
        {
            private readonly EsmApiPortProbeResult _result;
            public StubPortProbe(EsmApiPortProbeResult result) => _result = result;
            public Task<EsmApiPortProbeResult> CheckAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(_result);
            }
        }

        private sealed class StubLog : ILogService
        {
            public void LogUser(string message, bool isError = false) { }
            public void LogDebug(string message) { }
            public string GetUserLog() => string.Empty;
        }
    }
}

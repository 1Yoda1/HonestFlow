using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Installation;
using HonestFlow.Application.PointStatus;
using HonestFlow.Infrastructure.Api;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class EsmTsPiotRegistrationClientTests
    {
        [Fact]
        public async Task RegisterAsync_UsesFirstKktSerialAndPostsOnlyId()
        {
            string config = CreateConfig();
            var handler = new RecordingHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":{\"kkt\":[{\"kktSerial\":\"serial-for-test\",\"model\":\"АТОЛ\"}]}}")
                },
                new HttpResponseMessage(HttpStatusCode.OK));
            try
            {
                using var client = CreateClient(handler, config);

                TsPiotRegistrationResult result = await client.RegisterAsync(CancellationToken.None);

                Assert.Equal(TsPiotRegistrationStatus.Success, result.Status);
                Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Post }, handler.Methods);
                Assert.Equal("/api/v1/dkktList", handler.Paths[0]);
                Assert.Equal("/api/v1/tspiot", handler.Paths[1]);
                JObject body = JObject.Parse(handler.RequestBodies[1]);
                Assert.Single(body.Properties());
                Assert.Equal("serial-for-test", body.Value<string>("id"));
            }
            finally { File.Delete(config); }
        }

        [Theory]
        [InlineData("{\"data\":{\"kkt\":[]}}")]
        [InlineData("{\"kkt\":[{}]}")]
        public async Task RegisterAsync_EmptyOrSerialLessKkt_DoesNotPost(string json)
        {
            string config = CreateConfig();
            var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
            try
            {
                using var client = CreateClient(handler, config);

                TsPiotRegistrationResult result = await client.RegisterAsync(CancellationToken.None);

                Assert.Equal(TsPiotRegistrationStatus.KktNotDetected, result.Status);
                Assert.Single(handler.Methods);
                Assert.Equal(HttpMethod.Get, handler.Methods[0]);
            }
            finally { File.Delete(config); }
        }

        [Fact]
        public async Task RegisterAsync_EsmUnavailable_ReturnsEsmUnavailable()
        {
            string config = CreateConfig();
            var handler = new ThrowingHandler(new HttpRequestException("refused"));
            try
            {
                using var client = CreateClient(handler, config);

                TsPiotRegistrationResult result = await client.RegisterAsync(CancellationToken.None);

                Assert.Equal(TsPiotRegistrationStatus.EsmUnavailable, result.Status);
            }
            finally { File.Delete(config); }
        }

        [Fact]
        public async Task RegisterAsync_PostFailure_ReturnsRegistrationFailed()
        {
            string config = CreateConfig();
            var handler = new RecordingHandler(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"kkt\":[{\"kktSerial\":\"serial-for-test\"}]}") },
                new HttpResponseMessage(HttpStatusCode.BadRequest));
            try
            {
                using var client = CreateClient(handler, config);

                TsPiotRegistrationResult result = await client.RegisterAsync(CancellationToken.None);

                Assert.Equal(TsPiotRegistrationStatus.RegistrationFailed, result.Status);
            }
            finally { File.Delete(config); }
        }

        [Fact]
        public async Task RegisterAsync_WhenEsmCmServiceDoesNotAppear_ReturnsRegistrationFailed()
        {
            string config = CreateConfig();
            var handler = new RecordingHandler(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"kkt\":[{\"kktSerial\":\"serial-for-test\"}]}") },
                new HttpResponseMessage(HttpStatusCode.OK));
            try
            {
                using var client = new EsmTsPiotRegistrationClient(
                    new HttpClient(handler), config, ownsClient: true, serviceWaiter: new MissingServiceWaiter(),
                    registrationStatusClient: new StubEsmStatusClient(EsmRegistrationResult.Registered()));

                TsPiotRegistrationResult result = await client.RegisterAsync(CancellationToken.None);

                Assert.Equal(TsPiotRegistrationStatus.RegistrationFailed, result.Status);
            }
            finally { File.Delete(config); }
        }

        [Fact]
        public async Task RegisterAsync_ServiceAppearanceWithoutRegistrationConfirmation_ReturnsRegistrationFailed()
        {
            string config = CreateConfig();
            var handler = new HangingPostHandler();
            try
            {
                using var client = new EsmTsPiotRegistrationClient(
                    new HttpClient(handler), config, ownsClient: true, serviceWaiter: new CompletedServiceWaiter(),
                    registrationStatusClient: new StubEsmStatusClient(EsmRegistrationResult.NotConfigured()));

                TsPiotRegistrationResult result = await client.RegisterAsync(CancellationToken.None);

                Assert.Equal(TsPiotRegistrationStatus.RegistrationFailed, result.Status);
                Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Post }, handler.Methods);
            }
            finally { File.Delete(config); }
        }

        [Fact]
        public async Task RegisterAsync_ExternalCancellation_IsNotSwallowed()
        {
            string config = CreateConfig();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                using var client = CreateClient(
                    new ThrowingHandler(new OperationCanceledException(cancellation.Token)), config);

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.RegisterAsync(cancellation.Token));
            }
            finally { File.Delete(config); }
        }

        private static string CreateConfig()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            File.WriteAllText(path, "{\"port\":51888}");
            return path;
        }

        private static EsmTsPiotRegistrationClient CreateClient(HttpMessageHandler handler, string config) =>
            new(
                new HttpClient(handler),
                config,
                ownsClient: true,
                serviceWaiter: new CompletedServiceWaiter(),
                registrationStatusClient: new StubEsmStatusClient(EsmRegistrationResult.Registered()));

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly Queue<HttpResponseMessage> _responses;
            public RecordingHandler(params HttpResponseMessage[] responses) => _responses = new Queue<HttpResponseMessage>(responses);
            public List<HttpMethod> Methods { get; } = new();
            public List<string> Paths { get; } = new();
            public List<string> RequestBodies { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Methods.Add(request.Method);
                Paths.Add(request.RequestUri.AbsolutePath);
                RequestBodies.Add(request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
                return _responses.Dequeue();
            }
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            private readonly Exception _exception;
            public ThrowingHandler(Exception exception) => _exception = exception;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromException<HttpResponseMessage>(_exception);
        }

        private sealed class HangingPostHandler : HttpMessageHandler
        {
            public List<HttpMethod> Methods { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Methods.Add(request.Method);
                if (request.Method == HttpMethod.Get)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"kkt\":[{\"kktSerial\":\"serial-for-test\"}]}")
                    };
                }

                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable after cancellation.");
            }
        }

        private sealed class CompletedServiceWaiter : IEsmCmServiceRegistrationWaiter
        {
            public async Task<bool> WaitForServiceAsync(string registrationId, CancellationToken cancellationToken)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                return true;
            }
        }

        private sealed class MissingServiceWaiter : IEsmCmServiceRegistrationWaiter
        {
            public Task<bool> WaitForServiceAsync(string registrationId, CancellationToken cancellationToken) =>
                Task.FromResult(false);
        }

        private sealed class StubEsmStatusClient : IEsmStatusClient
        {
            private readonly EsmRegistrationResult _registration;

            public StubEsmStatusClient(EsmRegistrationResult registration) => _registration = registration;

            public Task<EsmStatusResult> GetStatusAsync(CancellationToken cancellationToken) =>
                Task.FromResult(EsmStatusResult.NotConfigured());

            public Task<EsmCashRegisterResult> GetCashRegisterStatusAsync(CancellationToken cancellationToken) =>
                Task.FromResult(EsmCashRegisterResult.NotConfigured());

            public Task<EsmRegistrationResult> GetRegistrationStatusAsync(CancellationToken cancellationToken) =>
                Task.FromResult(_registration);
        }
    }
}

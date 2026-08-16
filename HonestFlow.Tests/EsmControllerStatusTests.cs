using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.PointStatus;
using HonestFlow.Infrastructure.Api;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class EsmControllerStatusTests
    {
        [Fact]
        public async Task MissingConfig_IsNotConfigured()
        {
            using var client = new EsmRestStatusClient(new HttpClient(new StubHandler((Func<CancellationToken, HttpResponseMessage>)(_ => throw new Exception()))), Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
            Assert.Equal(EsmStatusResultKind.NotConfigured, (await client.GetStatusAsync(CancellationToken.None)).Kind);
        }

        [Fact]
        public async Task ApiUnavailable_IsUnavailable()
        {
            string config = CreateConfig();
            try
            {
                using var client = new EsmRestStatusClient(new HttpClient(new StubHandler((Func<CancellationToken, HttpResponseMessage>)(_ => throw new HttpRequestException()))), config);
                Assert.Equal(EsmStatusResultKind.Unavailable, (await client.GetStatusAsync(CancellationToken.None)).Kind);
            }
            finally { File.Delete(config); }
        }

        [Fact]
        public async Task SuccessfulStatus_ExposesRuntimeEsmPort()
        {
            string config = CreateConfig(51234);
            try
            {
                int request = 0;
                using var client = Client(config, _ => Json(request++ switch
                {
                    0 => "{\"instances\":[{\"id\":\"one\"}]}",
                    1 => "{\"lmController\":{\"code\":0},\"lm\":{\"code\":0}}",
                    _ => "{\"code\":0}"
                }));

                EsmStatusResult result = await client.GetStatusAsync(CancellationToken.None);

                Assert.Equal(EsmStatusResultKind.Success, result.Kind);
                Assert.Equal(51234, result.ApiPort);
            }
            finally { File.Delete(config); }
        }

        [Fact]
        public async Task Cancellation_IsPropagated()
        {
            string config = CreateConfig();
            try
            {
                using var client = Client(config, async token => { await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(HttpStatusCode.OK); });
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetStatusAsync(cancellation.Token));
            }
            finally { File.Delete(config); }
        }

        [Fact]
        public async Task UnknownJsonFieldsAreIgnored_AndMissingLinkCodeIsSafe()
        {
            string config = CreateConfig();
            try
            {
                int request = 0;
                using var client = Client(config, _ => Json(request++ switch
                {
                    0 => "{\"instances\":[{\"id\":\"one\",\"secret\":\"ignored\"}],\"extra\":1}",
                    1 => "{\"lmController\":{\"extra\":true},\"clientSoftware\":{\"id\":\"ignored\"}}",
                    _ => "{\"lmStatus\":{},\"unknown\":true}"
                }));
                EsmStatusResult result = await client.GetStatusAsync(CancellationToken.None);

                Assert.Equal(EsmStatusResultKind.Success, result.Kind);
                Assert.Null(result.Status.LmController.Code);
            }
            finally { File.Delete(config); }
        }

        [Fact]
        public async Task EmptyLmArray_PreservesSuccessfulEsmStatus()
        {
            string config = CreateConfig();
            try
            {
                int request = 0;
                using var client = Client(config, _ => Json(request++ switch
                {
                    0 => "{\"instances\":[{\"id\":\"one\"}]}",
                    1 => "{\"lmController\":{\"code\":0},\"lm\":{\"code\":0}}",
                    _ => "[]"
                }));

                EsmStatusResult result = await client.GetStatusAsync(CancellationToken.None);

                Assert.Equal(EsmStatusResultKind.Success, result.Kind);
                Assert.Null(result.Status.LmInfo);
            }
            finally { File.Delete(config); }
        }

        private static string CreateConfig(int port = 51077)
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            File.WriteAllText(path, "{\"port\":" + port + ",\"ignored\":true}");
            return path;
        }

        private static EsmRestStatusClient Client(string config, Func<CancellationToken, HttpResponseMessage> response) =>
            new(new HttpClient(new StubHandler(response)), config);

        private static EsmRestStatusClient Client(string config, Func<CancellationToken, Task<HttpResponseMessage>> response) =>
            new(new HttpClient(new StubHandler(response)), config);

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json) };

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<CancellationToken, Task<HttpResponseMessage>> _response;
            public StubHandler(Func<CancellationToken, HttpResponseMessage> response) => _response = token => Task.FromResult(response(token));
            public StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> response) => _response = response;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _response(cancellationToken);
        }
    }
}

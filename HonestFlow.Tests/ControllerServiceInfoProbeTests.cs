using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Api;
using Xunit;

namespace HonestFlow.Tests;

public sealed class ControllerServiceInfoProbeTests
{
    [Fact]
    public async Task SuccessfulResponse_IsAvailableAndDoesNotReadBody()
    {
        Uri requested = null;
        using var probe = new ControllerServiceInfoProbe(new HttpClient(new StubHandler(request =>
        {
            requested = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ignored") };
        })));

        var result = await probe.CheckAsync(CancellationToken.None);

        Assert.True(result.IsAvailable);
        Assert.Equal(200, result.HttpStatusCode);
        Assert.Equal("http://127.0.0.1:5063/api/v1/service-info", requested.ToString());
    }

    [Fact]
    public async Task NonSuccessResponse_IsUnavailable()
    {
        using var probe = new ControllerServiceInfoProbe(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));

        var result = await probe.CheckAsync(CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Equal(503, result.HttpStatusCode);
        Assert.Equal("http_error", result.ErrorCategory);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_response(request));
    }
}

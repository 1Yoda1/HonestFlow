using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Updates;
using Xunit;

namespace HonestFlow.Tests;

public sealed class PublicUpdateStreamingTests
{
    [Fact]
    public async Task Current_MapsSignedCloudReleaseContract()
    {
        const string json = "{\"application\":\"HonestFlow\",\"version\":\"3.1.0\",\"fileName\":\"HonestFlow.exe\",\"architecture\":\"x64\",\"sizeBytes\":123,\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"downloadUrl\":\"https://api.honestflow.ru/api/updates/honestflow/3.1.0/download\",\"releaseKeyId\":\"release-1\",\"manifestBase64\":\"bQ==\",\"releaseSignatureBase64\":\"cw==\"}";
        using var http = new HttpClient(new Handler(new StringContent(json, Encoding.UTF8, "application/json")))
        {
            BaseAddress = new Uri("https://api.honestflow.ru/")
        };
        using var client = new PublicHonestFlowUpdateClient(http);

        SelfUpdateInfo update = await client.GetCurrentAsync(CancellationToken.None);

        Assert.Equal("HonestFlow.exe", update.AssetName);
        Assert.Equal("x64", update.Architecture);
        Assert.Equal("release-1", update.ReleaseKeyId);
        Assert.Equal("bQ==", update.ManifestBase64);
        Assert.Equal("cw==", update.ReleaseSignatureBase64);
    }

    [Fact]
    public async Task Download_ReturnsHeadersWithoutBufferingExecutableBody()
    {
        var content = new DeferredContent();
        using var http = new HttpClient(new Handler(content));
        using var client = new PublicHonestFlowUpdateClient(http);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/update.exe");
        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);
        Assert.False(content.WasRead);
        Assert.Equal("executable", await response.Content.ReadAsStringAsync());
        Assert.True(content.WasRead);
    }

    private sealed class Handler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }

    private sealed class DeferredContent : HttpContent
    {
        public bool WasRead { get; private set; }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            WasRead = true;
            await stream.WriteAsync(Encoding.UTF8.GetBytes("executable"));
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}

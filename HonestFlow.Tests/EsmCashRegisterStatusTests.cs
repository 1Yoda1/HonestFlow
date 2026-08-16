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
    public sealed class EsmCashRegisterStatusTests
    {
        [Fact]
        public async Task DkktListWithNonEmptyKkt_IsConnected()
        {
            await AssertCashRegisterResult("{\"data\":{\"kkt\":[{}]}}", EsmCashRegisterResultKind.Connected);
        }

        [Theory]
        [InlineData("{\"data\":{\"kkt\":[]}}")]
        [InlineData("{\"kkt\":[]}")]
        [InlineData("{\"data\":{}}")]
        public async Task DkktListWithEmptyOrMissingKkt_IsDisconnected(string json)
        {
            await AssertCashRegisterResult(json, EsmCashRegisterResultKind.Disconnected);
        }

        private static async Task AssertCashRegisterResult(string json, EsmCashRegisterResultKind expected)
        {
            string config = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            File.WriteAllText(config, "{\"port\":51077}");
            try
            {
                using var client = new EsmRestStatusClient(new HttpClient(new StubHandler(json)), config, ownsClient: true);
                Assert.Equal(expected, (await client.GetCashRegisterStatusAsync(CancellationToken.None)).Kind);
            }
            finally
            {
                File.Delete(config);
            }
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly string _json;
            public StubHandler(string json) => _json = json;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_json) });
        }
    }
}

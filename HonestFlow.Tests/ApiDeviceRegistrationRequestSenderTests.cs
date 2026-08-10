using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.Licensing;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class ApiDeviceRegistrationRequestSenderTests
    {
        [Fact]
        public async Task SendsMinimalDeviceDtoToApi()
        {
            var session = new CapturingSession();
            var sender = new ApiDeviceRegistrationRequestSender(session);

            await sender.SendAsync("{\"deviceId\":\"device-1\",\"deviceName\":\"Workstation\",\"clientId\":\"client-1\"}", CancellationToken.None);

            Assert.Equal("api/device/request", session.Path);
            Assert.Contains("\"deviceId\":\"device-1\"", session.Body);
            Assert.Contains("\"name\":\"Workstation\"", session.Body);
            Assert.DoesNotContain("clientId", session.Body);
        }

        private sealed class CapturingSession : IApiSessionService
        {
            public string Path { get; private set; }
            public string Body { get; private set; }
            public Task<ApiTokenResponse> LoginAsync(string login, string password, string deviceId, string deviceName, CancellationToken cancellationToken) => throw new System.NotSupportedException();
            public async Task<HttpResponseMessage> SendAuthorizedAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Path = request.RequestUri.ToString();
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        }
    }
}

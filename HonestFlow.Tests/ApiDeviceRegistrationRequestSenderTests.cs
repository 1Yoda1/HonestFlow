using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.Licensing;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class ApiDeviceRegistrationRequestSenderTests
    {
        [Fact]
        public async Task SendsDeviceDtoWithPhysicalAddressToApi()
        {
            var session = new CapturingSession();
            var sender = new ApiDeviceRegistrationRequestSender(session);

            await sender.SendAsync("{\"deviceId\":\"device-1\",\"deviceName\":\"Workstation\",\"address\":\" ул. Ленина, 10 \",\"honestFlowVersion\":\"3.0.1.0\",\"clientId\":\"client-1\"}", CancellationToken.None);

            Assert.Equal("api/device/request", session.Path);
            JObject body = JObject.Parse(session.Body);
            Assert.Equal("device-1", body.Value<string>("deviceId"));
            Assert.Equal(Environment.MachineName, body.Value<string>("name"));
            Assert.Equal("ул. Ленина, 10", body.Value<string>("address"));
            Assert.Equal("3.0.1.0", body.Value<string>("honestFlowVersion"));
            Assert.Null(body["clientId"]);
        }

        [Fact]
        public async Task UsesMachineNameWhenDeviceNameIsMissing()
        {
            var session = new CapturingSession();
            var sender = new ApiDeviceRegistrationRequestSender(session);

            await sender.SendAsync("{\"deviceId\":\"device-1\",\"address\":\"ул. Мира, 5\"}", CancellationToken.None);

            Assert.Equal(Environment.MachineName, JObject.Parse(session.Body).Value<string>("name"));
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

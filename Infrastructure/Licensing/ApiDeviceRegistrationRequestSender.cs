using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Api;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Licensing
{
    public sealed class ApiDeviceRegistrationRequestSender : IDeviceRegistrationRequestSender
    {
        private readonly IApiSessionService _session;

        public ApiDeviceRegistrationRequestSender(IApiSessionService session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public async Task SendAsync(string requestJson, CancellationToken cancellationToken)
        {
            DeviceRegistrationRequest registration;
            try
            {
                registration = JsonConvert.DeserializeObject<DeviceRegistrationRequest>(requestJson);
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("Invalid device registration request.", nameof(requestJson), ex);
            }
            if (registration == null || string.IsNullOrWhiteSpace(registration.DeviceId))
                throw new ArgumentException("DeviceId is required.", nameof(requestJson));
            if (string.IsNullOrWhiteSpace(registration.Address) || registration.Address.Trim().Length > 300)
                throw new ArgumentException("Address must contain 1 to 300 characters.", nameof(requestJson));

            string payload = JsonConvert.SerializeObject(new
            {
                deviceId = registration.DeviceId,
                name = Environment.MachineName,
                address = registration.Address.Trim()
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/device/request")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            using HttpResponseMessage response = await _session.SendAuthorizedAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new ApiRequestException(response.StatusCode);
        }
    }
}

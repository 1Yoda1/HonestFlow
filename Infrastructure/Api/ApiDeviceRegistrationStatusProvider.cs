using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Api
{
    public sealed class ApiDeviceRegistrationStatusProvider : IDeviceRegistrationStatusProvider
    {
        private readonly IApiSessionService _session;

        public ApiDeviceRegistrationStatusProvider(IApiSessionService session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public async Task<DeviceRegistrationStatus> GetCurrentAsync(CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/device/registration/current");
            using HttpResponseMessage response = await _session.SendAuthorizedAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            if (!response.IsSuccessStatusCode)
                throw new ApiRequestException(response.StatusCode);

            ApiRegistrationStatusResponse status = JsonConvert.DeserializeObject<ApiRegistrationStatusResponse>(
                await response.Content.ReadAsStringAsync(cancellationToken));
            return status == null
                ? null
                : new DeviceRegistrationStatus
                {
                    DeviceId = status.DeviceId,
                    Status = status.Status,
                    Comment = status.Comment
                };
        }
    }
}

using System.Net;
using HonestFlow.Application.Auth;
using HonestFlow.Infrastructure.Api;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class ApiAuthenticationErrorPresentationTests
    {
        [Fact]
        public void CrossClientDeviceConflict_HasActionableMessage()
        {
            var exception = new ApiRequestException(
                HttpStatusCode.Conflict,
                ApiErrorCodes.DeviceBoundToAnotherClient);

            string message = ApiAuthenticationErrorPresentation.GetMessage(exception);

            Assert.Equal(
                "Этот компьютер уже зарегистрирован за другим клиентом. Для переноса устройства обратитесь к администратору.",
                message);
        }
    }
}

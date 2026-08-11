using HonestFlow.Infrastructure.Api;

namespace HonestFlow.Application.Auth
{
    public static class ApiAuthenticationErrorPresentation
    {
        public static string GetMessage(ApiRequestException exception) =>
            exception?.ErrorCode == ApiErrorCodes.DeviceBoundToAnotherClient
                ? "Этот компьютер уже зарегистрирован за другим клиентом. Для переноса устройства обратитесь к администратору."
                : null;
    }
}

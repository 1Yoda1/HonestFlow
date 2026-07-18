namespace HonestFlow.Application.DeviceIdentity
{
    public sealed class DeviceIdentityResult
    {
        private DeviceIdentityResult(
            DeviceIdentityStatus status,
            string deviceId,
            string errorCode)
        {
            Status = status;
            DeviceId = deviceId;
            ErrorCode = errorCode;
        }

        public DeviceIdentityStatus Status { get; }
        public string DeviceId { get; }
        public string ErrorCode { get; }
        public bool IsAvailable => Status != DeviceIdentityStatus.Unavailable;

        public static DeviceIdentityResult Available(
            DeviceIdentityStatus status,
            string deviceId) => new(status, deviceId, null);

        public static DeviceIdentityResult Unavailable(string errorCode) =>
            new(DeviceIdentityStatus.Unavailable, null, errorCode);
    }
}

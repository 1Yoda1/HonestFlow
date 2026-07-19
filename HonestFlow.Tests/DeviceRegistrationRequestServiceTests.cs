using System;
using HonestFlow.Application.Licensing;
using Newtonsoft.Json;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class DeviceRegistrationRequestServiceTests
    {
        [Fact]
        public void Create_ReturnsStrictRegistrationRequestWithUtcTimestamp()
        {
            DateTimeOffset localTime = new(2026, 7, 19, 10, 0, 0, TimeSpan.FromHours(6));
            string json = new DeviceRegistrationRequestService().Create(
                " client-1 ",
                " device-1 ",
                " PC-1 ",
                " ул. Ленина, 10 ",
                " 2.4.2.0 ",
                localTime);

            DeviceRegistrationRequest request =
                JsonConvert.DeserializeObject<DeviceRegistrationRequest>(json);

            Assert.Equal(1, request.SchemaVersion);
            Assert.Equal("client-1", request.ClientId);
            Assert.Equal("device-1", request.DeviceId);
            Assert.Equal("PC-1", request.DeviceName);
            Assert.Equal("ул. Ленина, 10", request.Address);
            Assert.Equal("2.4.2.0", request.HonestFlowVersion);
            Assert.Equal(TimeSpan.Zero, request.RequestedAtUtc.Offset);
        }

        [Theory]
        [InlineData(null, "device")]
        [InlineData("client", null)]
        public void Create_RejectsMissingIdentifiers(string clientId, string deviceId)
        {
            Assert.Throws<ArgumentException>(() =>
                new DeviceRegistrationRequestService().Create(
                    clientId,
                    deviceId,
                    "PC",
                    "1.0",
                    DateTimeOffset.UtcNow));
        }
    }
}

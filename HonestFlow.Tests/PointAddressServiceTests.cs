using System;
using System.IO;
using HonestFlow.Application.Core;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.PointIdentity;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class PointAddressServiceTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "honestflow-point-address-tests-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void Resolve_PrefersLicenseAddressAndPersistsItForSameDevice()
        {
            string stateFile = Path.Combine(_folder, "point-address.json");
            var service = CreateService(stateFile, () => "адрес из ККТ");

            PointAddressResult fromLicense = service.Resolve(Snapshot("device-1", "адрес лицензии"));
            PointAddressResult fromCache = CreateService(stateFile, () => null)
                .Resolve(Snapshot("device-1", null));

            Assert.Equal(PointAddressSource.License, fromLicense.Source);
            Assert.Equal("адрес лицензии", fromLicense.Address);
            Assert.Equal(PointAddressSource.LocalCache, fromCache.Source);
            Assert.Equal("адрес лицензии", fromCache.Address);
        }

        [Fact]
        public void Resolve_DoesNotReuseAddressForAnotherDevice()
        {
            string stateFile = Path.Combine(_folder, "point-address.json");
            var service = CreateService(stateFile, () => null);
            service.Save("device-1", "старый адрес", PointAddressSource.Manual);

            PointAddressResult result = service.Resolve(Snapshot("device-2", null));

            Assert.False(result.IsAvailable);
            Assert.Equal(PointAddressSource.None, result.Source);
        }

        [Fact]
        public void Resolve_UsesKktAddressAndPersistsIt()
        {
            string stateFile = Path.Combine(_folder, "point-address.json");

            PointAddressResult result = CreateService(stateFile, () => " адрес ККТ ")
                .Resolve(Snapshot("device-1", null));
            PointAddressResult cached = CreateService(stateFile, () => null)
                .Resolve(Snapshot("device-1", null));

            Assert.Equal(PointAddressSource.KktLog, result.Source);
            Assert.Equal("адрес ККТ", result.Address);
            Assert.Equal(PointAddressSource.LocalCache, cached.Source);
        }

        [Fact]
        public void Resolve_IgnoresCorruptStateWithoutThrowing()
        {
            Directory.CreateDirectory(_folder);
            string stateFile = Path.Combine(_folder, "point-address.json");
            File.WriteAllText(stateFile, "not json");

            PointAddressResult result = CreateService(stateFile, () => null)
                .Resolve(Snapshot("device-1", null));

            Assert.False(result.IsAvailable);
        }

        [Fact]
        public void NormalizeAddress_RejectsOverlongValue()
        {
            Assert.Null(PointAddressService.NormalizeAddress(new string('a', 501)));
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, true);
        }

        private static PointAddressService CreateService(string stateFile, Func<string> kktProvider) =>
            new(new TestLogService(), stateFile, kktProvider);

        private static LicenseObservationSnapshot Snapshot(string deviceId, string address) => new()
        {
            DeviceId = deviceId,
            PointAddress = address
        };

        private sealed class TestLogService : ILogService
        {
            public void LogUser(string message, bool isError = false) { }
            public void LogDebug(string message) { }
            public string GetUserLog() => string.Empty;
        }
    }
}

using System;
using System.IO;
using HonestFlow.Infrastructure.Licensing;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class TrustedLicenseClockTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "honestflow-license-clock-tests-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void TrustedTime_IsPersistedAndDoesNotMoveBackwardsAcrossInstances()
        {
            Directory.CreateDirectory(_folder);
            string path = Path.Combine(_folder, "clock.dpapi");
            DateTimeOffset trusted = DateTimeOffset.UtcNow.AddHours(2);
            var first = new DpapiTrustedLicenseClock(path);

            first.ObserveTrustedTime(
                trusted.AddMinutes(-1),
                trusted.AddDays(1),
                trusted);

            var second = new DpapiTrustedLicenseClock(path);
            Assert.True(second.UtcNow >= trusted);
        }

        [Fact]
        public void HttpsTimeOutsideSignedValidity_IsIgnored()
        {
            Directory.CreateDirectory(_folder);
            string path = Path.Combine(_folder, "clock.dpapi");
            DateTimeOffset issued = DateTimeOffset.UtcNow.AddMinutes(1);
            var clock = new DpapiTrustedLicenseClock(path);

            clock.ObserveTrustedTime(
                issued,
                issued.AddHours(1),
                issued.AddYears(10));

            Assert.True(clock.UtcNow < issued.AddDays(1));
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, true);
        }
    }
}

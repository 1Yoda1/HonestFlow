using System;
using System.IO;
using HonestFlow.Application.RemoteAccess;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class UnlicensedHelpRequestStoreTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "honestflow-help-request-tests-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void MarkSent_PersistsQuotaByClientAndDevice()
        {
            Directory.CreateDirectory(_folder);
            string path = Path.Combine(_folder, "quota.dpapi");
            var first = new UnlicensedHelpRequestStore(path);

            first.MarkSent("client-1", "device-1");

            var second = new UnlicensedHelpRequestStore(path);
            Assert.True(second.WasSent("client-1", "device-1"));
            Assert.False(second.WasSent("client-2", "device-1"));
            Assert.False(second.WasSent("client-1", "device-2"));
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, true);
        }
    }
}

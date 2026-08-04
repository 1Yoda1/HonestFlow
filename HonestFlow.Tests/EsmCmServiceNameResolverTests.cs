using System;
using System.IO;
using HonestFlow.Application.PointStatus;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class EsmCmServiceNameResolverTests
    {
        [Fact]
        public void ResolveFromLogs_ReturnsDynamicServiceName()
        {
            string folder = CreateFolder();
            try
            {
                string logFolder = Directory.CreateDirectory(Path.Combine(folder, "instance", "log")).FullName;
                File.WriteAllText(Path.Combine(logFolder, "esm-cm-shop-42.log"), "test");

                Assert.Equal(
                    "esm-cm-shop-42",
                    new EsmCmServiceNameResolver(folder).ResolveFromLogs());
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void ResolveFromLogs_SelectsMostRecentlyWrittenLog()
        {
            string folder = CreateFolder();
            try
            {
                string oldLog = Path.Combine(folder, "esm-cm-old.log");
                string currentLog = Path.Combine(folder, "esm-cm-current.log");
                File.WriteAllText(oldLog, "old");
                File.WriteAllText(currentLog, "current");
                File.SetLastWriteTimeUtc(oldLog, DateTime.UtcNow.AddDays(-1));
                File.SetLastWriteTimeUtc(currentLog, DateTime.UtcNow);

                Assert.Equal(
                    "esm-cm-current",
                    new EsmCmServiceNameResolver(folder).ResolveFromLogs());
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void ResolveFromLogs_IgnoresUnrelatedLogFiles()
        {
            string folder = CreateFolder();
            try
            {
                File.WriteAllText(Path.Combine(folder, "esm-orchestrator.log"), "test");
                Assert.Null(new EsmCmServiceNameResolver(folder).ResolveFromLogs());
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Theory]
        [InlineData("esm-cm-one", true)]
        [InlineData("ESM-CM-store-17", true)]
        [InlineData("esm-cm-", false)]
        [InlineData("esm-orchestrator", false)]
        [InlineData(null, false)]
        public void IsValidServiceName_ValidatesPrefixAndSuffix(string value, bool expected)
        {
            Assert.Equal(expected, EsmCmServiceNameResolver.IsValidServiceName(value));
        }

        private static string CreateFolder()
        {
            string folder = Path.Combine(Path.GetTempPath(), "HonestFlow.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }
    }
}

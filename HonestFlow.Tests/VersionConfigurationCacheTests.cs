using System;
using System.IO;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Models;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class VersionConfigurationCacheTests
    {
        [Fact]
        public void SaveAndLoad_PreservesVersions()
        {
            string directory = Path.Combine(Path.GetTempPath(), "HonestFlowTests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "versions.json");
            try
            {
                var cache = new VersionConfigurationCache(path);
                cache.Save(new VersionsData
                {
                    LmModule = "2.6.0",
                    AtolDriver = "10.10.8.24",
                    ESM = "1.6.3.2",
                    Controller = "1.6.3.2"
                });

                VersionsData result = cache.Load();

                Assert.Equal("2.6.0", result.LmModule);
                Assert.Equal("10.10.8.24", result.AtolDriver);
                Assert.Equal("1.6.3.2", result.ESM);
                Assert.Equal("1.6.3.2", result.Controller);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void Load_DamagedFile_ReturnsNull()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            try
            {
                File.WriteAllText(path, "not-json");
                Assert.Null(new VersionConfigurationCache(path).Load());
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}

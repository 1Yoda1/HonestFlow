using System;
using System.IO;
using HonestFlow.Infrastructure.Configuration;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class InstallerCacheLocationStoreTests
    {
        [Fact]
        public void RegisterLocations_RemembersExistingCache_WithoutCopyingItsFiles()
        {
            using var scope = new TemporaryDirectory();
            string legacyCache = Directory.CreateDirectory(Path.Combine(scope.Path, "old-cache")).FullName;
            string installer = Path.Combine(legacyCache, "large-installer.exe");
            File.WriteAllBytes(installer, new byte[] { 1, 2, 3, 4 });
            string stateFile = Path.Combine(scope.Path, "state", "locations.json");

            var store = new InstallerCacheLocationStore(stateFile);
            int registered = store.RegisterLocations(new[] { legacyCache });

            Assert.Equal(1, registered);
            Assert.Equal(new[] { legacyCache }, store.ReadLocations());
            Assert.True(File.Exists(installer));
            string[] files = Directory.GetFiles(scope.Path, "*", SearchOption.AllDirectories);
            Assert.Equal(2, files.Length);
            Assert.Contains(installer, files);
            Assert.Contains(stateFile, files);
        }

        [Fact]
        public void RegisterLocations_IsIdempotent()
        {
            using var scope = new TemporaryDirectory();
            string legacyCache = Directory.CreateDirectory(Path.Combine(scope.Path, "old-cache")).FullName;
            File.WriteAllText(Path.Combine(legacyCache, "installer.msi"), "payload");
            var store = new InstallerCacheLocationStore(Path.Combine(scope.Path, "locations.json"));

            Assert.Equal(1, store.RegisterLocations(new[] { legacyCache }));
            Assert.Equal(0, store.RegisterLocations(new[] { legacyCache }));
            Assert.Single(store.ReadLocations());
        }

        [Fact]
        public void RegisterLocations_IgnoresEmptyAndPartialOnlyFolders()
        {
            using var scope = new TemporaryDirectory();
            string empty = Directory.CreateDirectory(Path.Combine(scope.Path, "empty")).FullName;
            string partial = Directory.CreateDirectory(Path.Combine(scope.Path, "partial")).FullName;
            File.WriteAllText(Path.Combine(partial, "installer.exe.download"), "partial");
            var store = new InstallerCacheLocationStore(Path.Combine(scope.Path, "locations.json"));

            Assert.Equal(0, store.RegisterLocations(new[] { empty, partial }));
            Assert.Empty(store.ReadLocations());
        }

        [Fact]
        public void ReadLocations_CorruptedMetadata_ReturnsEmpty()
        {
            using var scope = new TemporaryDirectory();
            string stateFile = Path.Combine(scope.Path, "locations.json");
            File.WriteAllText(stateFile, "not-json");

            Assert.Empty(new InstallerCacheLocationStore(stateFile).ReadLocations());
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "HonestFlow-cache-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Path, recursive: true);
                }
                catch
                {
                }
            }
        }
    }
}

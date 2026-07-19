using System;
using System.IO;
using HonestFlow.Application.Prerequisites;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class DotNetDesktopRuntimeInstallerTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "honestflow-dotnet-runtime-tests-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void PackageMetadata_MatchesPublishedVerifiedAsset()
        {
            Assert.Equal(
                "windowsdesktop-runtime-10.0.10-win-x64.exe",
                DotNetDesktopRuntimeInstaller.PackageFileName);
            Assert.Equal(60053808, DotNetDesktopRuntimeInstaller.PackageSize);
            Assert.Equal(
                "E82FC901C8F52D716293B2BC0830CE0DD254A06268C457A19E8FC503560A84D1",
                DotNetDesktopRuntimeInstaller.PackageSha256);
        }

        [Fact]
        public void RuntimeDetection_RequiresExpectedOrNewerNet10Patch()
        {
            Directory.CreateDirectory(Path.Combine(_folder, "9.0.10"));
            Directory.CreateDirectory(Path.Combine(_folder, "10.0.9"));
            Directory.CreateDirectory(Path.Combine(_folder, "invalid"));

            Assert.False(DotNetDesktopRuntimeInstaller.IsRequiredRuntimeInstalled(
                _folder,
                new Version(10, 0, 10)));

            Directory.CreateDirectory(Path.Combine(_folder, "10.0.10"));

            Assert.True(DotNetDesktopRuntimeInstaller.IsRequiredRuntimeInstalled(
                _folder,
                new Version(10, 0, 10)));
        }

        [Theory]
        [InlineData(1073741823, false)]
        [InlineData(1073741824, true)]
        [InlineData(2147483648, true)]
        public void DiskSpaceCheck_RequiresOneGiB(long availableBytes, bool expected)
        {
            Assert.Equal(
                expected,
                DotNetDesktopRuntimeInstaller.HasSufficientFreeSpace(availableBytes));
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(3010, true)]
        [InlineData(1603, false)]
        public void ExitCodeClassification_UsesOfficialInstallerCodes(
            int exitCode,
            bool expected)
        {
            Assert.Equal(
                expected,
                DotNetDesktopRuntimeInstaller.IsSuccessfulExitCode(exitCode));
        }

        [Fact]
        public void PayloadValidation_RejectsUnexpectedSize()
        {
            Directory.CreateDirectory(_folder);
            string path = Path.Combine(_folder, DotNetDesktopRuntimeInstaller.PackageFileName);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

            string error = DotNetDesktopRuntimeInstaller.ValidatePayload(path);

            Assert.Contains("Размер", error);
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, true);
        }
    }
}

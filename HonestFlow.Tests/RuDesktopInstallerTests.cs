using HonestFlow.Application.RemoteAccess;
using System;
using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class RuDesktopInstallerTests : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "HonestFlow-RuDesktopInstallerTests-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void PackageSelection_UsesOperatingSystemArchitecture()
        {
            RuDesktopPackage x64 = RuDesktopInstaller.GetPackageForOperatingSystem(true);
            RuDesktopPackage x86 = RuDesktopInstaller.GetPackageForOperatingSystem(false);

            Assert.Equal("rudesktop-3.0.1563-x64.msi", x64.FileName);
            Assert.Equal(31748096, x64.Size);
            Assert.Equal("rudesktop-3.0.1563-x32.msi", x86.FileName);
            Assert.Equal(22806528, x86.Size);
        }

        [Fact]
        public void ValidatePayload_AcceptsExpectedSizeAndHash()
        {
            byte[] payload = { 1, 2, 3, 4, 5 };
            string path = CreateFile(payload);
            string hash = Convert.ToHexString(SHA256.HashData(payload));

            string error = RuDesktopInstaller.ValidatePayload(path, payload.Length, hash);

            Assert.Null(error);
        }

        [Fact]
        public void ValidatePayload_RejectsUnexpectedSize()
        {
            string path = CreateFile(new byte[] { 1, 2, 3 });

            string error = RuDesktopInstaller.ValidatePayload(path, 4, "unused");

            Assert.Contains("Размер", error);
        }

        [Fact]
        public void ValidatePayload_RejectsUnexpectedHash()
        {
            byte[] payload = { 1, 2, 3 };
            string path = CreateFile(payload);

            string error = RuDesktopInstaller.ValidatePayload(
                path,
                payload.Length,
                new string('0', 64));

            Assert.Contains("SHA-256", error);
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(3010, true)]
        [InlineData(1603, false)]
        [InlineData(1618, false)]
        public void ExitCodeClassification_ReturnsExpectedResult(int exitCode, bool expected)
        {
            Assert.Equal(expected, RuDesktopInstaller.IsSuccessfulExitCode(exitCode));
        }

        private string CreateFile(byte[] payload)
        {
            Directory.CreateDirectory(_directory);
            string path = Path.Combine(_directory, "package.msi");
            File.WriteAllBytes(path, payload);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}

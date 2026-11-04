using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Infrastructure.Updates;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class SelfUpdateServiceTests : IDisposable
    {
        private const string ReleaseKeyId = "release-test";
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "HonestFlow.Tests", Guid.NewGuid().ToString("N"));
        private readonly ECDsa _releaseKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        [Theory]
        [InlineData(@"C:\Apps\HonestFlow.exe")]
        [InlineData(@"C:\Apps\HonestFlow (1).exe")]
        [InlineData(@"C:\Apps\Renamed.exe")]
        public void ResolveCanonicalUpdateTarget_AlwaysUsesCanonicalFileName(string runningExe)
        {
            string target = (string)InvokeStatic("ResolveCanonicalUpdateTarget", runningExe);
            Assert.Equal(@"C:\Apps\HonestFlow.exe", target);
        }

        [Fact]
        public void BuildUpdateScript_RechecksServerSizeAndSha256BeforeReplacement()
        {
            string script = (string)InvokeStatic(
                "BuildUpdateScript",
                @"C:\Users\Test\Downloads\HonestFlow (1).exe",
                @"C:\Users\Test\Downloads\HonestFlow.exe",
                @"C:\ProgramData\HonestFlow\update\HonestFlow.new.exe",
                @"C:\Apps\HonestFlow.backup.exe",
                @"C:\Users\Test\Downloads",
                @"C:\Apps\HonestFlow.update.log",
                1234,
                new string('a', 64),
                2048L,
                "3.1.0");

            Assert.Contains("Get-FileHash -LiteralPath $newExe -Algorithm SHA256", script);
            Assert.Contains("$expectedSha256", script);
            Assert.Contains("$expectedSizeBytes", script);
            Assert.Contains("Copy-Item -LiteralPath $newExe -Destination $targetExe -Force", script);
            Assert.Contains("Get-FileHash -LiteralPath $targetExe -Algorithm SHA256", script);
            Assert.True(script.IndexOf("Get-FileHash -LiteralPath $targetExe -Algorithm SHA256",
                StringComparison.Ordinal) > script.IndexOf("Copy-Item -LiteralPath $newExe -Destination $targetExe -Force",
                StringComparison.Ordinal));
            Assert.Contains("$backupCreated", script);
        }

        [Fact]
        public void UpdateCommand_IsEncodedAndDoesNotExecuteScriptFromDisk()
        {
            const string script = "Write-Output 'verified update'";
            var startInfo = (ProcessStartInfo)InvokeStatic("BuildPowerShellStartInfo", script, @"C:\Apps");

            Assert.Equal("powershell.exe", Path.GetFileName(startInfo.FileName), ignoreCase: true);
            Assert.Equal("-EncodedCommand", startInfo.ArgumentList[^2]);
            Assert.Equal(script, Encoding.Unicode.GetString(Convert.FromBase64String(startInfo.ArgumentList[^1])));
            Assert.DoesNotContain("-File", startInfo.ArgumentList);
            Assert.True(Path.IsPathFullyQualified(startInfo.FileName));
        }

        [Fact]
        public async Task WrongSha256_DoesNotApplyUpdate()
        {
            byte[] good = "expected update"u8.ToArray();
            var updateClient = new BytesUpdateClient("tampered update"u8.ToArray(), Asset(good));
            var dialogs = new RecordingDialogs();
            bool applied = false;
            var service = CreateService(updateClient, dialogs, () => applied = true);

            bool result = await service.CheckDownloadAndRunUpdateIfNeeded();

            Assert.False(result);
            Assert.False(applied);
            Assert.NotEmpty(dialogs.Errors);
        }

        [Fact]
        public async Task MissingSha256_DoesNotRequestOrApplyUpdate()
        {
            byte[] bytes = "expected update"u8.ToArray();
            var update = Asset(bytes);
            update.Sha256 = null;
            var updateClient = new BytesUpdateClient(bytes, update);
            var dialogs = new RecordingDialogs();
            bool applied = false;
            var service = CreateService(updateClient, dialogs, () => applied = true);

            bool result = await service.CheckDownloadAndRunUpdateIfNeeded();

            Assert.False(result);
            Assert.False(applied);
            Assert.Equal(0, updateClient.DownloadCalls);
        }

        [Fact]
        public async Task RequiredPublisherSignature_RejectsLegacyUpdateBeforeDownload()
        {
            byte[] bytes = "legacy unsigned update"u8.ToArray();
            var updateClient = new BytesUpdateClient(bytes, UnsignedAsset(bytes));
            var dialogs = new RecordingDialogs();
            bool applied = false;
            var service = new SelfUpdateService(
                updateClient, dialogs,
                currentVersion: () => new Version(1, 0, 0),
                downloadedVersion: _ => new Version(2, 0, 0),
                applyUpdate: (_, _, _, _) => applied = true,
                updateRoot: _folder,
                releaseVerifier: ReleaseTrustStore.CreateVerifier(),
                requireSignedManifest: true);

            bool result = await service.CheckDownloadAndRunUpdateIfNeeded();

            Assert.False(result);
            Assert.False(applied);
            Assert.Equal(0, updateClient.DownloadCalls);
            Assert.NotEmpty(dialogs.Errors);
        }

        [Fact]
        public async Task Unavailable_public_update_endpoint_does_not_apply_or_require_session()
        {
            var dialogs = new RecordingDialogs();
            bool applied = false;
            var service = CreateService(new BytesUpdateClient(Array.Empty<byte>(), null), dialogs, () => applied = true);

            bool result = await service.CheckDownloadAndRunUpdateIfNeeded();

            Assert.False(result);
            Assert.False(applied);
            Assert.Empty(dialogs.Errors);
        }

        [Fact]
        public async Task VerifiedServerUpdate_HandsOnlyVerifiedFileToApplyStep()
        {
            byte[] bytes = "verified update"u8.ToArray();
            var updateClient = new BytesUpdateClient(bytes, Asset(bytes));
            var dialogs = new RecordingDialogs();
            string appliedPath = null;
            string appliedHash = null;
            var service = new SelfUpdateService(
                updateClient,
                dialogs,
                currentVersion: () => new Version(1, 0, 0),
                downloadedVersion: _ => new Version(2, 0, 0),
                applyUpdate: (path, sha, _, _) => { appliedPath = path; appliedHash = sha; },
                updateRoot: _folder,
                releaseVerifier: TestReleaseVerifier());

            bool result = await service.CheckDownloadAndRunUpdateIfNeeded();

            Assert.True(result);
            Assert.NotNull(appliedPath);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), appliedHash, ignoreCase: true);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(appliedPath));
        }

        [Fact]
        public void FromCurrentConfiguration_UsesServerMetadataIncludingShaAndSize()
        {
            var info = SelfUpdateInfo.FromCurrentConfiguration(new ApiConfigurationResponse
            {
                Components =
                {
                    new ApiComponentConfiguration
                    {
                        Component = "HonestFlow", EffectiveVersion = "2.0.0", FileName = "HonestFlow.exe",
                        DownloadUrl = "https://api.honestflow.ru/api/assets/HonestFlow/2.0.0/download",
                        Sha256 = new string('b', 64), SizeBytes = 123
                    }
                }
            });

            Assert.Equal("2.0.0", info.Version);
            Assert.Equal(new string('b', 64), info.Sha256);
            Assert.Equal(123, info.SizeBytes);
        }

        public void Dispose()
        {
            _releaseKey.Dispose();
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, recursive: true);
        }

        private SelfUpdateService CreateService(
            IPublicHonestFlowUpdateClient updateClient,
            RecordingDialogs dialogs,
            Action onApply) => new(
                updateClient,
                dialogs,
                currentVersion: () => new Version(1, 0, 0),
                downloadedVersion: _ => new Version(2, 0, 0),
                applyUpdate: (_, _, _, _) => onApply(),
                updateRoot: _folder,
                releaseVerifier: TestReleaseVerifier());

        private SelfUpdateInfo Asset(byte[] bytes)
        {
            string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            byte[] manifest = Encoding.UTF8.GetBytes(
                $"{{\"component\":\"HonestFlow\",\"version\":\"2.0.0\",\"fileName\":\"HonestFlow-test.exe\",\"architecture\":\"x64\",\"sizeBytes\":{bytes.Length},\"sha256\":\"{sha}\"}}");
            return new SelfUpdateInfo
            {
                Version = "2.0.0",
                AssetName = "HonestFlow-test.exe",
                Architecture = "x64",
                DownloadUrl = "https://api.honestflow.ru/api/assets/HonestFlow/2.0.0/download",
                Sha256 = sha,
                SizeBytes = bytes.LongLength,
                ReleaseKeyId = ReleaseKeyId,
                ManifestBase64 = Convert.ToBase64String(manifest),
                ReleaseSignatureBase64 = Convert.ToBase64String(_releaseKey.SignData(
                    manifest, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            };
        }

        private static SelfUpdateInfo UnsignedAsset(byte[] bytes) => new()
        {
            Version = "2.0.0",
            AssetName = "HonestFlow-test.exe",
            Architecture = "x64",
            DownloadUrl = "https://api.honestflow.ru/api/assets/HonestFlow/2.0.0/download",
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            SizeBytes = bytes.LongLength
        };

        private ReleaseManifestVerifier TestReleaseVerifier() => new(
            new Dictionary<string, byte[]>
            {
                [ReleaseKeyId] = _releaseKey.ExportSubjectPublicKeyInfo()
            });

        private static object InvokeStatic(string name, params object[] arguments)
        {
            MethodInfo method = typeof(SelfUpdateService).GetMethod(name,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return method.Invoke(null, arguments);
        }

        private sealed class BytesUpdateClient : IPublicHonestFlowUpdateClient
        {
            private readonly byte[] _bytes;
            private readonly SelfUpdateInfo _metadata;
            public BytesUpdateClient(byte[] bytes, SelfUpdateInfo? metadata)
            {
                _bytes = bytes;
                _metadata = metadata;
            }
            public int DownloadCalls { get; private set; }
            public Task<SelfUpdateInfo?> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(_metadata);
            public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                DownloadCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_bytes)
                });
            }
        }

        private sealed class RecordingDialogs : IUserDialogService
        {
            public System.Collections.Generic.List<string> Errors { get; } = new();
            public void ShowInformation(string message, string title) { }
            public void ShowWarning(string message, string title) { }
            public void ShowError(string message, string title) => Errors.Add(message);
            public bool Confirm(string message, string title, UserDialogIcon icon = UserDialogIcon.Warning) => true;
        }
    }
}

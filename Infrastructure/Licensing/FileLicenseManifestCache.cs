using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Models.Licensing;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Licensing
{
    public sealed class FileLicenseManifestCache : ILicenseManifestCache
    {
        private const string ModuleName = nameof(FileLicenseManifestCache);
        private const string CurrentPointerFileName = "current";
        private const string ManifestFileName = "licenses.json";
        private const string SignatureFileName = "licenses.json.sig";
        private const string MetadataFileName = "metadata.dpapi";
        private readonly string _cacheRoot;
        private readonly ILicenseSignatureVerifier _signatureVerifier;
        private readonly ILicenseCacheMetadataProtector _metadataProtector;

        public FileLicenseManifestCache(
            ILicenseSignatureVerifier signatureVerifier,
            ILicenseCacheMetadataProtector metadataProtector)
            : this(AppPaths.LicenseCacheFolder, signatureVerifier, metadataProtector)
        {
        }

        public FileLicenseManifestCache(
            string cacheRoot,
            ILicenseSignatureVerifier signatureVerifier,
            ILicenseCacheMetadataProtector metadataProtector)
        {
            if (string.IsNullOrWhiteSpace(cacheRoot))
                throw new ArgumentException("Cache root is required.", nameof(cacheRoot));

            _cacheRoot = Path.GetFullPath(cacheRoot);
            _signatureVerifier = signatureVerifier ?? throw new ArgumentNullException(nameof(signatureVerifier));
            _metadataProtector = metadataProtector ?? throw new ArgumentNullException(nameof(metadataProtector));
        }

        public async Task<LicenseCacheWriteResult> SaveAsync(
            LicenseManifestReadResult onlineResult,
            DateTimeOffset successfulOnlineCheckUtc,
            CancellationToken cancellationToken)
        {
            if (onlineResult == null || !onlineResult.IsSuccess)
                return LicenseCacheWriteResult.Failure(LicenseCacheStatus.WriteFailed, "OnlineReadNotSuccessful");

            byte[] manifestBytes = onlineResult.ManifestBytes.ToArray();
            byte[] signatureFileBytes = onlineResult.SignatureFileBytes.ToArray();
            if (!TryValidateSnapshot(manifestBytes, signatureFileBytes, out LicenseManifest manifest, out string errorCode))
                return LicenseCacheWriteResult.Failure(LicenseCacheStatus.WriteFailed, errorCode);

            LicenseCacheReadResult existing = await ReadAsync(cancellationToken);
            if (existing.IsSuccess && existing.Manifest.Revision > manifest.Revision)
            {
                Logger.Warning(
                    $"Event=LicenseCacheWriteSkipped Status=StaleRevision " +
                    $"IncomingRevision={manifest.Revision} CachedRevision={existing.Manifest.Revision}",
                    ModuleName);
                return LicenseCacheWriteResult.Failure(LicenseCacheStatus.StaleRevision, "RevisionOlderThanCache");
            }

            string temporarySnapshot = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(_cacheRoot);
                string snapshotName = "snapshot-" + Guid.NewGuid().ToString("N");
                temporarySnapshot = Path.Combine(_cacheRoot, ".tmp-" + Guid.NewGuid().ToString("N"));
                string finalSnapshot = Path.Combine(_cacheRoot, snapshotName);
                Directory.CreateDirectory(temporarySnapshot);

                var metadata = new LicenseCacheMetadata
                {
                    LastSuccessfulOnlineCheckUtc = successfulOnlineCheckUtc.ToUniversalTime(),
                    SchemaVersion = manifest.SchemaVersion,
                    Revision = manifest.Revision,
                    ManifestSha256 = ComputeSha256(manifestBytes),
                    SignatureFileSha256 = ComputeSha256(signatureFileBytes)
                };
                byte[] metadataBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(metadata));
                byte[] protectedMetadata = _metadataProtector.Protect(metadataBytes);

                await WriteDurableFileAsync(
                    Path.Combine(temporarySnapshot, ManifestFileName),
                    manifestBytes,
                    cancellationToken);
                await WriteDurableFileAsync(
                    Path.Combine(temporarySnapshot, SignatureFileName),
                    signatureFileBytes,
                    cancellationToken);
                await WriteDurableFileAsync(
                    Path.Combine(temporarySnapshot, MetadataFileName),
                    protectedMetadata,
                    cancellationToken);

                Directory.Move(temporarySnapshot, finalSnapshot);
                temporarySnapshot = null;
                await ReplaceCurrentPointerAsync(snapshotName, cancellationToken);

                Logger.Info(
                    $"Event=LicenseCacheWriteFinished Status=Success SchemaVersion={manifest.SchemaVersion} " +
                    $"Revision={manifest.Revision}",
                    ModuleName);
                return LicenseCacheWriteResult.Success();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is CryptographicException ||
                ex is JsonException)
            {
                Logger.Warning(
                    $"Event=LicenseCacheWriteFinished Status=WriteFailed ErrorType={ex.GetType().Name}",
                    ModuleName);
                return LicenseCacheWriteResult.Failure(LicenseCacheStatus.WriteFailed, "CacheWriteFailed");
            }
            finally
            {
                if (temporarySnapshot != null)
                {
                    try
                    {
                        if (Directory.Exists(temporarySnapshot))
                            Directory.Delete(temporarySnapshot, true);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public async Task<LicenseCacheReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            try
            {
                string pointerPath = Path.Combine(_cacheRoot, CurrentPointerFileName);
                if (!File.Exists(pointerPath))
                    return LicenseCacheReadResult.Failure(LicenseCacheStatus.NotFound, "CacheNotFound");

                string snapshotName = (await File.ReadAllTextAsync(pointerPath, cancellationToken)).Trim();
                if (!IsValidSnapshotName(snapshotName))
                    return InvalidCache("InvalidSnapshotPointer");

                string snapshotPath = Path.Combine(_cacheRoot, snapshotName);
                string resolvedSnapshotPath = Path.GetFullPath(snapshotPath);
                if (!resolvedSnapshotPath.StartsWith(_cacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return InvalidCache("InvalidSnapshotPath");

                byte[] manifestBytes = await File.ReadAllBytesAsync(
                    Path.Combine(snapshotPath, ManifestFileName),
                    cancellationToken);
                byte[] signatureFileBytes = await File.ReadAllBytesAsync(
                    Path.Combine(snapshotPath, SignatureFileName),
                    cancellationToken);
                byte[] protectedMetadata = await File.ReadAllBytesAsync(
                    Path.Combine(snapshotPath, MetadataFileName),
                    cancellationToken);
                byte[] metadataBytes = _metadataProtector.Unprotect(protectedMetadata);
                var metadata = JsonConvert.DeserializeObject<LicenseCacheMetadata>(
                    new UTF8Encoding(false, true).GetString(metadataBytes));

                if (metadata == null || metadata.MetadataVersion != 1)
                    return InvalidCache("InvalidMetadata");

                if (!FixedEquals(metadata.ManifestSha256, ComputeSha256(manifestBytes)) ||
                    !FixedEquals(metadata.SignatureFileSha256, ComputeSha256(signatureFileBytes)))
                {
                    return InvalidCache("CacheHashMismatch");
                }

                if (!TryValidateSnapshot(manifestBytes, signatureFileBytes, out LicenseManifest manifest, out string errorCode))
                    return InvalidCache(errorCode);

                if (metadata.Revision != manifest.Revision || metadata.SchemaVersion != manifest.SchemaVersion)
                    return InvalidCache("MetadataManifestMismatch");

                Logger.Info(
                    $"Event=LicenseCacheReadFinished Status=Success SchemaVersion={manifest.SchemaVersion} " +
                    $"Revision={manifest.Revision}",
                    ModuleName);
                return LicenseCacheReadResult.Success(
                    manifest,
                    metadata.LastSuccessfulOnlineCheckUtc.ToUniversalTime());
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is CryptographicException ||
                ex is JsonException ||
                ex is DecoderFallbackException)
            {
                Logger.Warning(
                    $"Event=LicenseCacheReadFinished Status=InvalidCache ErrorType={ex.GetType().Name}",
                    ModuleName);
                return InvalidCache("CacheReadFailed");
            }
        }

        private bool TryValidateSnapshot(
            byte[] manifestBytes,
            byte[] signatureFileBytes,
            out LicenseManifest manifest,
            out string errorCode)
        {
            manifest = null;
            errorCode = null;
            if (manifestBytes == null || manifestBytes.Length == 0 ||
                signatureFileBytes == null || signatureFileBytes.Length == 0)
            {
                errorCode = "CacheFilesEmpty";
                return false;
            }

            LicenseSignatureVerificationResult signatureResult = _signatureVerifier.Verify(
                manifestBytes,
                signatureFileBytes);
            if (!signatureResult.IsValid)
            {
                errorCode = signatureResult.ErrorCode ?? "CacheSignatureInvalid";
                return false;
            }

            try
            {
                string json = new UTF8Encoding(false, true).GetString(manifestBytes);
                manifest = JsonConvert.DeserializeObject<LicenseManifest>(json);
            }
            catch (JsonException)
            {
                errorCode = "CacheJsonInvalid";
                return false;
            }
            catch (DecoderFallbackException)
            {
                errorCode = "CacheJsonEncodingInvalid";
                return false;
            }

            if (manifest == null)
            {
                errorCode = "CacheManifestNull";
                return false;
            }

            if (LicenseManifestValidator.Validate(manifest).Count > 0)
            {
                errorCode = "CacheManifestValidationFailed";
                return false;
            }

            return true;
        }

        private async Task ReplaceCurrentPointerAsync(
            string snapshotName,
            CancellationToken cancellationToken)
        {
            string pointerPath = Path.Combine(_cacheRoot, CurrentPointerFileName);
            string temporaryPointer = Path.Combine(_cacheRoot, ".current-" + Guid.NewGuid().ToString("N") + ".tmp");
            await WriteDurableFileAsync(
                temporaryPointer,
                Encoding.UTF8.GetBytes(snapshotName),
                cancellationToken);

            if (File.Exists(pointerPath))
            {
                string backup = Path.Combine(_cacheRoot, ".current-backup");
                File.Replace(temporaryPointer, pointerPath, backup, true);
                try
                {
                    if (File.Exists(backup))
                        File.Delete(backup);
                }
                catch
                {
                }
            }
            else
            {
                File.Move(temporaryPointer, pointerPath);
            }
        }

        private static async Task WriteDurableFileAsync(
            string path,
            byte[] bytes,
            CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(true);
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using SHA256 sha256 = SHA256.Create();
            return Convert.ToBase64String(sha256.ComputeHash(bytes));
        }

        private static bool FixedEquals(string left, string right)
        {
            if (left == null || right == null)
                return false;

            byte[] leftBytes = Encoding.UTF8.GetBytes(left);
            byte[] rightBytes = Encoding.UTF8.GetBytes(right);
            return leftBytes.Length == rightBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }

        private static bool IsValidSnapshotName(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.StartsWith("snapshot-", StringComparison.Ordinal) &&
                   value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
                   value.IndexOf(Path.DirectorySeparatorChar) < 0 &&
                   value.IndexOf(Path.AltDirectorySeparatorChar) < 0;
        }

        private static LicenseCacheReadResult InvalidCache(string errorCode) =>
            LicenseCacheReadResult.Failure(LicenseCacheStatus.InvalidCache, errorCode);
    }
}

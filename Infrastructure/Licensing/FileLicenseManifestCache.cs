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
        private const string GrantFileName = "grant.json";
        private const string SignatureFileName = "grant.json.sig";
        private const string MetadataFileName = "metadata.dpapi";
        private const string HighestRevisionFileName = "highest-revision.dpapi";
        private readonly string _cacheRoot;
        private readonly ILicenseSignatureVerifier _signatureVerifier;
        private readonly ILicenseCacheMetadataProtector _metadataProtector;
        private readonly bool _isSubjectCache;

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
            : this(cacheRoot, signatureVerifier, metadataProtector, false)
        {
        }

        private FileLicenseManifestCache(
            string cacheRoot,
            ILicenseSignatureVerifier signatureVerifier,
            ILicenseCacheMetadataProtector metadataProtector,
            bool isSubjectCache)
        {
            if (string.IsNullOrWhiteSpace(cacheRoot))
                throw new ArgumentException("Cache root is required.", nameof(cacheRoot));

            _cacheRoot = Path.GetFullPath(cacheRoot);
            _signatureVerifier = signatureVerifier ?? throw new ArgumentNullException(nameof(signatureVerifier));
            _metadataProtector = metadataProtector ?? throw new ArgumentNullException(nameof(metadataProtector));
            _isSubjectCache = isSubjectCache;
        }

        public async Task<LicenseCacheWriteResult> SaveAsync(
            LicenseGrantRequest request,
            LicenseManifestReadResult onlineResult,
            DateTimeOffset successfulOnlineCheckUtc,
            CancellationToken cancellationToken)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (!_isSubjectCache)
                return await ForSubject(request).SaveAsync(
                    request,
                    onlineResult,
                    successfulOnlineCheckUtc,
                    cancellationToken);
            if (onlineResult == null || !onlineResult.IsSuccess)
                return LicenseCacheWriteResult.Failure(LicenseCacheStatus.WriteFailed, "OnlineReadNotSuccessful");

            byte[] grantBytes = onlineResult.GrantBytes.ToArray();
            byte[] signatureFileBytes = onlineResult.SignatureFileBytes.ToArray();
            if (!TryValidateSnapshot(grantBytes, signatureFileBytes, out LicenseGrant grant, out string errorCode) ||
                !GrantMatchesRequest(grant, request))
                return LicenseCacheWriteResult.Failure(LicenseCacheStatus.WriteFailed, errorCode);

            LicenseCacheReadResult existing = await ReadAsync(request, cancellationToken);
            long highestRevision = ReadHighestRevision();
            if ((existing.IsSuccess && existing.Grant.Revision > grant.Revision) ||
                highestRevision > grant.Revision)
            {
                Logger.Warning(
                    $"Event=LicenseCacheWriteSkipped Status=StaleRevision " +
                    $"IncomingRevision={grant.Revision} HighestRevision={Math.Max(highestRevision, existing.Grant?.Revision ?? 0)}",
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
                    SchemaVersion = grant.SchemaVersion,
                    Revision = grant.Revision,
                    GrantSha256 = ComputeSha256(grantBytes),
                    SignatureFileSha256 = ComputeSha256(signatureFileBytes)
                };
                byte[] metadataBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(metadata));
                byte[] protectedMetadata = _metadataProtector.Protect(metadataBytes);

                await WriteDurableFileAsync(
                    Path.Combine(temporarySnapshot, GrantFileName),
                    grantBytes,
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
                WriteHighestRevision(grant.Revision);
                await ReplaceCurrentPointerAsync(snapshotName, cancellationToken);
                DeleteOldSnapshots(snapshotName);

                Logger.Info(
                    $"Event=LicenseCacheWriteFinished Status=Success SchemaVersion={grant.SchemaVersion} " +
                    $"Revision={grant.Revision}",
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

        public async Task<LicenseCacheReadResult> ReadAsync(
            LicenseGrantRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (!_isSubjectCache)
            {
                LicenseCacheReadResult subject = await ForSubject(request)
                    .ReadAsync(request, cancellationToken);
                if (subject.Status != LicenseCacheStatus.NotFound ||
                    !string.Equals(
                        _cacheRoot,
                        Path.GetFullPath(AppPaths.LicenseCacheFolder),
                        StringComparison.OrdinalIgnoreCase))
                    return subject;

                return await ReadLegacySharedCacheAsync(request, cancellationToken);
            }
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

                byte[] grantBytes = await File.ReadAllBytesAsync(
                    Path.Combine(snapshotPath, GrantFileName),
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

                if (metadata == null || metadata.MetadataVersion != 2)
                    return InvalidCache("InvalidMetadata");

                if (!FixedEquals(metadata.GrantSha256, ComputeSha256(grantBytes)) ||
                    !FixedEquals(metadata.SignatureFileSha256, ComputeSha256(signatureFileBytes)))
                {
                    return InvalidCache("CacheHashMismatch");
                }

                if (!TryValidateSnapshot(grantBytes, signatureFileBytes, out LicenseGrant grant, out string errorCode))
                    return InvalidCache(errorCode);

                if (!GrantMatchesRequest(grant, request))
                    return InvalidCache("GrantSubjectMismatch");

                if (metadata.Revision != grant.Revision || metadata.SchemaVersion != grant.SchemaVersion)
                    return InvalidCache("MetadataManifestMismatch");

                if (grant.Revision < ReadHighestRevision())
                    return InvalidCache("CacheRevisionRollbackDetected");

                Logger.Info(
                    $"Event=LicenseCacheReadFinished Status=Success SchemaVersion={grant.SchemaVersion} " +
                    $"Revision={grant.Revision}",
                    ModuleName);
                return LicenseCacheReadResult.Success(
                    grant,
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
            byte[] grantBytes,
            byte[] signatureFileBytes,
            out LicenseGrant grant,
            out string errorCode)
        {
            grant = null;
            errorCode = null;
            if (grantBytes == null || grantBytes.Length == 0 ||
                signatureFileBytes == null || signatureFileBytes.Length == 0)
            {
                errorCode = "CacheFilesEmpty";
                return false;
            }

            LicenseSignatureVerificationResult signatureResult = _signatureVerifier.Verify(
                grantBytes,
                signatureFileBytes);
            if (!signatureResult.IsValid)
            {
                errorCode = signatureResult.ErrorCode ?? "CacheSignatureInvalid";
                return false;
            }

            try
            {
                string json = new UTF8Encoding(false, true).GetString(grantBytes);
                grant = JsonConvert.DeserializeObject<LicenseGrant>(json);
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

            if (grant == null)
            {
                errorCode = "CacheGrantNull";
                return false;
            }

            if (LicenseGrantValidator.Validate(grant).Count > 0)
            {
                errorCode = "CacheGrantValidationFailed";
                return false;
            }

            return true;
        }

        private static bool GrantMatchesRequest(
            LicenseGrant grant,
            LicenseGrantRequest request) =>
            grant != null &&
            string.Equals(grant.ClientId, request.ClientId, StringComparison.Ordinal) &&
            string.Equals(grant.DeviceId, request.DeviceId, StringComparison.Ordinal);

        private FileLicenseManifestCache ForSubject(LicenseGrantRequest request) =>
            new(
                Path.Combine(_cacheRoot, request.GetOpaquePathId()),
                _signatureVerifier,
                _metadataProtector,
                true);

        private async Task<LicenseCacheReadResult> ReadLegacySharedCacheAsync(
            LicenseGrantRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                string root = Path.GetFullPath(AppPaths.LegacyLicenseCacheFolder);
                string pointerPath = Path.Combine(root, CurrentPointerFileName);
                if (!File.Exists(pointerPath))
                    return LicenseCacheReadResult.Failure(LicenseCacheStatus.NotFound, "LegacyCacheNotFound");

                string snapshotName = (await File.ReadAllTextAsync(pointerPath, cancellationToken)).Trim();
                if (!IsValidSnapshotName(snapshotName))
                    return InvalidCache("InvalidLegacySnapshotPointer");
                string snapshotPath = Path.GetFullPath(Path.Combine(root, snapshotName));
                if (!snapshotPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return InvalidCache("InvalidLegacySnapshotPath");

                byte[] manifestBytes = await File.ReadAllBytesAsync(
                    Path.Combine(snapshotPath, "licenses.json"), cancellationToken);
                byte[] signatureBytes = await File.ReadAllBytesAsync(
                    Path.Combine(snapshotPath, "licenses.json.sig"), cancellationToken);
                byte[] metadataBytes = _metadataProtector.Unprotect(await File.ReadAllBytesAsync(
                    Path.Combine(snapshotPath, MetadataFileName), cancellationToken));
                LegacyCacheMetadata metadata = JsonConvert.DeserializeObject<LegacyCacheMetadata>(
                    new UTF8Encoding(false, true).GetString(metadataBytes));
                if (metadata == null || metadata.MetadataVersion != 1 ||
                    !FixedEquals(metadata.ManifestSha256, ComputeSha256(manifestBytes)) ||
                    !FixedEquals(metadata.SignatureFileSha256, ComputeSha256(signatureBytes)))
                    return InvalidCache("InvalidLegacyMetadata");

                LicenseSignatureVerificationResult signature = _signatureVerifier.Verify(
                    manifestBytes,
                    signatureBytes);
                if (!signature.IsValid)
                    return InvalidCache(signature.ErrorCode ?? "LegacyCacheSignatureInvalid");

                LicenseManifest manifest = JsonConvert.DeserializeObject<LicenseManifest>(
                    new UTF8Encoding(false, true).GetString(manifestBytes));
                LicenseGrant grant = LegacyLicenseGrantExtractor.Extract(manifest, request);
                if (grant == null)
                    return LicenseCacheReadResult.Failure(LicenseCacheStatus.NotFound, "LegacyGrantNotFound");

                Logger.Info(
                    $"Event=LegacyLicenseCacheFallback Status=Success Revision={grant.Revision}",
                    ModuleName);
                return LicenseCacheReadResult.Success(
                    grant,
                    metadata.LastSuccessfulOnlineCheckUtc.ToUniversalTime());
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is IOException || ex is UnauthorizedAccessException ||
                ex is CryptographicException || ex is JsonException ||
                ex is DecoderFallbackException)
            {
                Logger.Warning(
                    $"Event=LegacyLicenseCacheFallback Status=Failed ErrorType={ex.GetType().Name}",
                    ModuleName);
                return InvalidCache("LegacyCacheReadFailed");
            }
        }

        private sealed class LegacyCacheMetadata
        {
            public int MetadataVersion { get; set; }
            public DateTimeOffset LastSuccessfulOnlineCheckUtc { get; set; }
            public string ManifestSha256 { get; set; }
            public string SignatureFileSha256 { get; set; }
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

        private long ReadHighestRevision()
        {
            string path = Path.Combine(_cacheRoot, HighestRevisionFileName);
            if (!File.Exists(path))
                return 0;
            byte[] plaintext = _metadataProtector.Unprotect(File.ReadAllBytes(path));
            return long.TryParse(Encoding.UTF8.GetString(plaintext), out long revision) && revision >= 0
                ? revision
                : throw new InvalidDataException("Invalid highest license revision.");
        }

        private void WriteHighestRevision(long revision)
        {
            long existing = ReadHighestRevision();
            if (existing >= revision)
                return;
            string path = Path.Combine(_cacheRoot, HighestRevisionFileName);
            string temporary = Path.Combine(_cacheRoot, ".highest-revision-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllBytes(temporary, _metadataProtector.Protect(Encoding.UTF8.GetBytes(revision.ToString())));
            if (File.Exists(path))
                File.Replace(temporary, path, null, true);
            else
                File.Move(temporary, path);
        }

        private void DeleteOldSnapshots(string currentSnapshotName)
        {
            foreach (string directory in Directory.EnumerateDirectories(_cacheRoot, "snapshot-*"))
            {
                if (!string.Equals(Path.GetFileName(directory), currentSnapshotName, StringComparison.Ordinal))
                {
                    try { Directory.Delete(directory, true); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        Logger.Warning($"Event=LicenseCacheCleanupFailed ErrorType={ex.GetType().Name}", ModuleName);
                    }
                }
            }
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

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Downloads;
using HonestFlow.Models.Licensing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HonestFlow.Infrastructure.Licensing
{
    public sealed class YandexPointerLicenseManifestRepository : ILicenseManifestRepository
    {
        private const string ModuleName = nameof(YandexPointerLicenseManifestRepository);
        private const int MaxPointerBytes = 64 * 1024;
        private readonly HttpClient _httpClient;
        private readonly string _publicKey;
        private readonly TimeSpan _requestTimeout;
        private readonly int _maxManifestBytes;
        private readonly ILicenseSignatureVerifier _signatureVerifier;

        public YandexPointerLicenseManifestRepository(
            HttpClient httpClient,
            string publicKey,
            TimeSpan requestTimeout,
            int maxManifestBytes,
            ILicenseSignatureVerifier signatureVerifier)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _publicKey = string.IsNullOrWhiteSpace(publicKey)
                ? throw new ArgumentException("Yandex public key is required.", nameof(publicKey))
                : publicKey;
            _requestTimeout = requestTimeout > TimeSpan.Zero
                ? requestTimeout
                : throw new ArgumentOutOfRangeException(nameof(requestTimeout));
            _maxManifestBytes = maxManifestBytes > 0
                ? maxManifestBytes
                : throw new ArgumentOutOfRangeException(nameof(maxManifestBytes));
            _signatureVerifier = signatureVerifier ?? throw new ArgumentNullException(nameof(signatureVerifier));
        }

        public async Task<LicenseManifestReadResult> ReadAsync(
            LicenseGrantRequest grantRequest,
            CancellationToken cancellationToken)
        {
            if (grantRequest == null)
                throw new ArgumentNullException(nameof(grantRequest));

            using var timeoutSource = new CancellationTokenSource(_requestTimeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

            try
            {
                string grantRoot = "/licenses/grants/" + grantRequest.GetOpaquePathId();
                Uri pointerUrl;
                try
                {
                    pointerUrl = await ResolveDownloadUrlAsync(
                        grantRoot + "/grant-current.json",
                        linkedSource.Token);
                }
                catch (PointerNotFoundException)
                {
                    return await ReadLegacyGrantAsync(grantRequest, linkedSource.Token);
                }
                byte[] pointerBytes = await ReadBytesAsync(pointerUrl, MaxPointerBytes, linkedSource.Token);
                YandexLicensePublicationPointer pointer = ParseAndValidatePointer(pointerBytes, grantRoot);

                Uri manifestUrl = await ResolveDownloadUrlAsync(
                    pointer.VersionPath + "/grant.json",
                    linkedSource.Token);
                Uri signatureUrl = await ResolveDownloadUrlAsync(
                    pointer.VersionPath + "/grant.json.sig",
                    linkedSource.Token);

                var repository = new HttpLicenseManifestRepository(
                    _httpClient,
                    new LicenseManifestRepositoryOptions
                    {
                        ManifestUrl = manifestUrl,
                        SignatureUrl = signatureUrl,
                        RequestTimeout = _requestTimeout,
                        MaxResponseBytes = _maxManifestBytes,
                        SupportedSchemaVersion = 1
                    },
                    _signatureVerifier);

                LicenseManifestReadResult result = await repository.ReadAsync(grantRequest, linkedSource.Token);
                if (!result.IsSuccess)
                    return result;

                if (result.Grant.Revision != pointer.Revision)
                    return InvalidPointer("PointerRevisionMismatch");

                if (!FixedEquals(pointer.GrantSha256, ComputeSha256(result.GrantBytes.Span)) ||
                    !FixedEquals(pointer.SignatureSha256, ComputeSha256(result.SignatureFileBytes.Span)))
                {
                    return InvalidPointer("PointerHashMismatch");
                }

                Logger.Info(
                    $"Event=LicensePointerReadFinished Status=Success Revision={pointer.Revision}",
                    ModuleName);
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return LicenseManifestReadResult.Failure(
                    LicenseManifestReadStatus.Timeout,
                    "PointerRequestTimeout");
            }
            catch (HttpRequestException)
            {
                return LicenseManifestReadResult.Failure(
                    LicenseManifestReadStatus.NetworkUnavailable,
                    "PointerNetworkUnavailable");
            }
            catch (PointerNotFoundException)
            {
                return LicenseManifestReadResult.Failure(
                    LicenseManifestReadStatus.NotFound,
                    "PointerNotFound");
            }
            catch (InvalidDataException ex)
            {
                Logger.Warning(
                    $"Event=LicensePointerReadFinished Status=Invalid ErrorCode={ex.Message}",
                    ModuleName);
                return InvalidPointer(ex.Message);
            }
            catch (IOException)
            {
                return LicenseManifestReadResult.Failure(
                    LicenseManifestReadStatus.NetworkUnavailable,
                    "PointerResponseReadFailed");
            }
        }

        private async Task<LicenseManifestReadResult> ReadLegacyGrantAsync(
            LicenseGrantRequest request,
            CancellationToken cancellationToken)
        {
            Uri pointerUrl = await ResolveDownloadUrlAsync(
                "/licenses/licenses-current.json",
                cancellationToken);
            byte[] pointerBytes = await ReadBytesAsync(pointerUrl, MaxPointerBytes, cancellationToken);
            LegacyPublicationPointer pointer;
            try
            {
                pointer = JsonConvert.DeserializeObject<LegacyPublicationPointer>(
                    new UTF8Encoding(false, true).GetString(pointerBytes));
            }
            catch (Exception ex) when (ex is JsonException || ex is DecoderFallbackException)
            {
                return InvalidPointer("InvalidLegacyPointer");
            }

            string expectedPath = pointer == null || pointer.Revision < 0
                ? null
                : "/licenses/versions/revision-" + pointer.Revision.ToString("D20");
            if (pointer == null ||
                !string.Equals(pointer.VersionPath, expectedPath, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(pointer.ManifestSha256) ||
                string.IsNullOrWhiteSpace(pointer.SignatureSha256))
                return InvalidPointer("InvalidLegacyPointer");

            Uri manifestUrl = await ResolveDownloadUrlAsync(pointer.VersionPath + "/licenses.json", cancellationToken);
            Uri signatureUrl = await ResolveDownloadUrlAsync(pointer.VersionPath + "/licenses.json.sig", cancellationToken);
            byte[] manifestBytes = await ReadBytesAsync(manifestUrl, _maxManifestBytes, cancellationToken);
            byte[] signatureBytes = await ReadBytesAsync(signatureUrl, 16 * 1024, cancellationToken);
            if (!FixedEquals(pointer.ManifestSha256, ComputeSha256(manifestBytes)) ||
                !FixedEquals(pointer.SignatureSha256, ComputeSha256(signatureBytes)))
                return InvalidPointer("LegacyPointerHashMismatch");

            LicenseSignatureVerificationResult signature = _signatureVerifier.Verify(
                manifestBytes,
                signatureBytes);
            if (!signature.IsValid)
                return LicenseManifestReadResult.Failure(
                    LicenseManifestReadStatus.InvalidSignature,
                    signature.ErrorCode ?? "LegacySignatureInvalid");

            LicenseManifest manifest;
            try
            {
                manifest = JsonConvert.DeserializeObject<LicenseManifest>(
                    new UTF8Encoding(false, true).GetString(manifestBytes));
            }
            catch (Exception ex) when (ex is JsonException || ex is DecoderFallbackException)
            {
                return LicenseManifestReadResult.Failure(
                    LicenseManifestReadStatus.InvalidJson,
                    "LegacyManifestInvalid");
            }

            LicenseGrant grant = LegacyLicenseGrantExtractor.Extract(manifest, request);
            if (grant == null)
                return LicenseManifestReadResult.Failure(LicenseManifestReadStatus.NotFound, "LegacyGrantNotFound");

            byte[] grantBytes = new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(grant));
            Logger.Info(
                $"Event=LegacyLicenseFallback Status=Success Revision={grant.Revision}",
                ModuleName);
            return LicenseManifestReadResult.Success(
                grant,
                grantBytes,
                signatureBytes,
                cacheable: false);
        }

        private async Task<Uri> ResolveDownloadUrlAsync(string path, CancellationToken cancellationToken)
        {
            var apiUrl = new Uri(YandexDiskDownloader.BuildPublicDownloadUrl(path, _publicKey));
            byte[] responseBytes = await ReadBytesAsync(apiUrl, MaxPointerBytes, cancellationToken);
            try
            {
                var payload = JObject.Parse(new UTF8Encoding(false, true).GetString(responseBytes));
                string href = (string)payload["href"];
                if (!Uri.TryCreate(href, UriKind.Absolute, out Uri result) ||
                    (result.Scheme != Uri.UriSchemeHttp && result.Scheme != Uri.UriSchemeHttps))
                {
                    throw new InvalidDataException("InvalidYandexDownloadUrl");
                }

                return result;
            }
            catch (JsonException)
            {
                throw new InvalidDataException("InvalidYandexDownloadResponse");
            }
            catch (DecoderFallbackException)
            {
                throw new InvalidDataException("InvalidYandexDownloadResponseEncoding");
            }
        }

        private async Task<byte[]> ReadBytesAsync(
            Uri url,
            int maxBytes,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new PointerNotFoundException();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("Yandex request failed.");
            if (response.Content.Headers.ContentLength > maxBytes)
                throw new InvalidDataException("PointerResponseTooLarge");

            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var output = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                int read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (read == 0)
                    return output.ToArray();
                if (output.Length + read > maxBytes)
                    throw new InvalidDataException("PointerResponseTooLarge");
                output.Write(buffer, 0, read);
            }
        }

        private static YandexLicensePublicationPointer ParseAndValidatePointer(
            byte[] bytes,
            string grantRoot)
        {
            if (bytes == null || bytes.Length == 0)
                throw new InvalidDataException("EmptyLicensePointer");

            YandexLicensePublicationPointer pointer;
            try
            {
                pointer = JsonConvert.DeserializeObject<YandexLicensePublicationPointer>(
                    new UTF8Encoding(false, true).GetString(bytes));
            }
            catch (JsonException)
            {
                throw new InvalidDataException("InvalidLicensePointerJson");
            }
            catch (DecoderFallbackException)
            {
                throw new InvalidDataException("InvalidLicensePointerEncoding");
            }

            string expectedPath = pointer == null || pointer.Revision < 0
                ? null
                : grantRoot + "/versions/revision-" + pointer.Revision.ToString("D20");
            if (pointer == null ||
                !string.Equals(pointer.VersionPath, expectedPath, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(pointer.GrantSha256) ||
                string.IsNullOrWhiteSpace(pointer.SignatureSha256))
            {
                throw new InvalidDataException("InvalidLicensePointer");
            }

            return pointer;
        }

        private static string ComputeSha256(ReadOnlySpan<byte> bytes)
        {
            using SHA256 sha256 = SHA256.Create();
            return Convert.ToBase64String(sha256.ComputeHash(bytes.ToArray()));
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

        private static LicenseManifestReadResult InvalidPointer(string errorCode) =>
            LicenseManifestReadResult.Failure(LicenseManifestReadStatus.InvalidJson, errorCode);

        private sealed class PointerNotFoundException : Exception
        {
        }

        private sealed class LegacyPublicationPointer
        {
            public long Revision { get; set; }
            public string VersionPath { get; set; }
            public string ManifestSha256 { get; set; }
            public string SignatureSha256 { get; set; }
        }
    }
}

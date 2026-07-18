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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HonestFlow.Infrastructure.Licensing
{
    public sealed class YandexPointerLicenseManifestRepository : ILicenseManifestRepository
    {
        private const string ModuleName = nameof(YandexPointerLicenseManifestRepository);
        private const string PointerPath = "/licenses/licenses-current.json";
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

        public async Task<LicenseManifestReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            using var timeoutSource = new CancellationTokenSource(_requestTimeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

            try
            {
                Uri pointerUrl = await ResolveDownloadUrlAsync(PointerPath, linkedSource.Token);
                byte[] pointerBytes = await ReadBytesAsync(pointerUrl, MaxPointerBytes, linkedSource.Token);
                YandexLicensePublicationPointer pointer = ParseAndValidatePointer(pointerBytes);

                Uri manifestUrl = await ResolveDownloadUrlAsync(
                    pointer.VersionPath + "/licenses.json",
                    linkedSource.Token);
                Uri signatureUrl = await ResolveDownloadUrlAsync(
                    pointer.VersionPath + "/licenses.json.sig",
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

                LicenseManifestReadResult result = await repository.ReadAsync(linkedSource.Token);
                if (!result.IsSuccess)
                    return result;

                if (result.Manifest.Revision != pointer.Revision)
                    return InvalidPointer("PointerRevisionMismatch");

                if (!FixedEquals(pointer.ManifestSha256, ComputeSha256(result.ManifestBytes.Span)) ||
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

        private static YandexLicensePublicationPointer ParseAndValidatePointer(byte[] bytes)
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
                : "/licenses/versions/revision-" + pointer.Revision.ToString("D20");
            if (pointer == null ||
                !string.Equals(pointer.VersionPath, expectedPath, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(pointer.ManifestSha256) ||
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
    }
}

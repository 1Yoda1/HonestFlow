using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Models.Licensing;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Licensing
{
    public sealed class HttpLicenseManifestRepository : ILicenseManifestRepository
    {
        private const string ModuleName = nameof(HttpLicenseManifestRepository);
        private readonly HttpClient _httpClient;
        private readonly LicenseManifestRepositoryOptions _options;
        private readonly ILicenseSignatureVerifier _signatureVerifier;

        public HttpLicenseManifestRepository(
            HttpClient httpClient,
            LicenseManifestRepositoryOptions options,
            ILicenseSignatureVerifier signatureVerifier)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _signatureVerifier = signatureVerifier ?? throw new ArgumentNullException(nameof(signatureVerifier));
            _options.Validate();
        }

        public async Task<LicenseManifestReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            Logger.Info(
                $"Event=LicenseManifestReadStarted Host={_options.ManifestUrl.Host} " +
                $"TimeoutMs={_options.RequestTimeout.TotalMilliseconds:F0} MaxBytes={_options.MaxResponseBytes}",
                ModuleName);

            using var timeoutSource = new CancellationTokenSource(_options.RequestTimeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _options.ManifestUrl);
                using HttpResponseMessage response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    linkedSource.Token);

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return LogFailure(LicenseManifestReadStatus.NotFound, "Http404", response, stopwatch);

                if (!response.IsSuccessStatusCode)
                    return LogFailure(LicenseManifestReadStatus.ServerError, "HttpError", response, stopwatch);

                long? contentLength = response.Content.Headers.ContentLength;
                if (contentLength > _options.MaxResponseBytes)
                {
                    return LogFailure(
                        LicenseManifestReadStatus.ServerError,
                        "ResponseTooLarge",
                        response,
                        stopwatch);
                }

                byte[] bytes = await ReadResponseBytesAsync(
                    response,
                    _options.MaxResponseBytes,
                    linkedSource.Token);
                if (bytes.Length == 0)
                    return LogFailure(LicenseManifestReadStatus.InvalidJson, "EmptyResponse", response, stopwatch);

                using var signatureRequest = new HttpRequestMessage(HttpMethod.Get, _options.SignatureUrl);
                using HttpResponseMessage signatureResponse = await _httpClient.SendAsync(
                    signatureRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    linkedSource.Token);

                if (signatureResponse.StatusCode == HttpStatusCode.NotFound)
                    return LogFailure(LicenseManifestReadStatus.NotFound, "SignatureHttp404", signatureResponse, stopwatch);

                if (!signatureResponse.IsSuccessStatusCode)
                {
                    return LogFailure(
                        LicenseManifestReadStatus.ServerError,
                        "SignatureHttpError",
                        signatureResponse,
                        stopwatch);
                }

                if (signatureResponse.Content.Headers.ContentLength > _options.MaxSignatureResponseBytes)
                {
                    return LogFailure(
                        LicenseManifestReadStatus.ServerError,
                        "SignatureResponseTooLarge",
                        signatureResponse,
                        stopwatch);
                }

                byte[] signatureFileBytes = await ReadResponseBytesAsync(
                    signatureResponse,
                    _options.MaxSignatureResponseBytes,
                    linkedSource.Token);
                LicenseSignatureVerificationResult signatureResult = _signatureVerifier.Verify(
                    bytes,
                    signatureFileBytes);
                if (!signatureResult.IsValid)
                {
                    LicenseManifestReadStatus readStatus =
                        signatureResult.Status == LicenseSignatureVerificationStatus.UnknownKeyId
                            ? LicenseManifestReadStatus.UnknownKey
                            : LicenseManifestReadStatus.InvalidSignature;
                    Logger.Warning(
                        $"Event=LicenseManifestSignatureVerification Status={readStatus} " +
                        $"ErrorCode={signatureResult.ErrorCode} ElapsedMs={stopwatch.ElapsedMilliseconds}",
                        ModuleName);
                    return LicenseManifestReadResult.Failure(readStatus, signatureResult.ErrorCode);
                }

                LicenseManifest manifest;
                try
                {
                    string json = new UTF8Encoding(false, true).GetString(bytes);
                    manifest = JsonConvert.DeserializeObject<LicenseManifest>(json);
                }
                catch (JsonException)
                {
                    return LogFailure(LicenseManifestReadStatus.InvalidJson, "MalformedJson", response, stopwatch);
                }
                catch (DecoderFallbackException)
                {
                    return LogFailure(LicenseManifestReadStatus.InvalidJson, "InvalidUtf8", response, stopwatch);
                }

                if (manifest == null)
                    return LogFailure(LicenseManifestReadStatus.InvalidJson, "NullManifest", response, stopwatch);

                if (manifest.SchemaVersion != _options.SupportedSchemaVersion)
                {
                    Logger.Warning(
                        $"Event=LicenseManifestReadFinished Status=UnsupportedSchema " +
                        $"SchemaVersion={manifest.SchemaVersion} SupportedSchemaVersion={_options.SupportedSchemaVersion} " +
                        $"Bytes={bytes.Length} ElapsedMs={stopwatch.ElapsedMilliseconds}",
                        ModuleName);
                    return LicenseManifestReadResult.Failure(
                        LicenseManifestReadStatus.UnsupportedSchema,
                        "UnsupportedSchemaVersion");
                }

                var validationErrors = LicenseManifestValidator.Validate(manifest);
                if (validationErrors.Count > 0)
                {
                    Logger.Warning(
                        $"Event=LicenseManifestReadFinished Status=InvalidManifest " +
                        $"ValidationErrorCount={validationErrors.Count} ElapsedMs={stopwatch.ElapsedMilliseconds}",
                        ModuleName);
                    return LicenseManifestReadResult.Failure(
                        LicenseManifestReadStatus.InvalidManifest,
                        "ManifestValidationFailed");
                }

                Logger.Info(
                    $"Event=LicenseManifestReadFinished Status=Success SchemaVersion={manifest.SchemaVersion} " +
                    $"Revision={manifest.Revision} Bytes={bytes.Length} ElapsedMs={stopwatch.ElapsedMilliseconds}",
                    ModuleName);
                return LicenseManifestReadResult.Success(manifest, bytes, signatureFileBytes);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return LogFailure(LicenseManifestReadStatus.Timeout, "RequestTimeout", stopwatch);
            }
            catch (HttpRequestException)
            {
                return LogFailure(
                    LicenseManifestReadStatus.NetworkUnavailable,
                    "HttpRequestFailed",
                    stopwatch);
            }
            catch (ResponseTooLargeException)
            {
                return LogFailure(
                    LicenseManifestReadStatus.ServerError,
                    "ResponseTooLarge",
                    stopwatch);
            }
            catch (IOException)
            {
                return LogFailure(
                    LicenseManifestReadStatus.NetworkUnavailable,
                    "ResponseReadFailed",
                    stopwatch);
            }
        }

        private async Task<byte[]> ReadResponseBytesAsync(
            HttpResponseMessage response,
            int maxResponseBytes,
            CancellationToken cancellationToken)
        {
            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var output = new MemoryStream();
            var buffer = new byte[16 * 1024];

            while (true)
            {
                int bytesRead = await input.ReadAsync(
                    buffer,
                    0,
                    buffer.Length,
                    cancellationToken);
                if (bytesRead == 0)
                    break;

                if (output.Length + bytesRead > maxResponseBytes)
                    throw new ResponseTooLargeException();

                output.Write(buffer, 0, bytesRead);
            }

            return output.ToArray();
        }

        private static LicenseManifestReadResult LogFailure(
            LicenseManifestReadStatus status,
            string errorCode,
            HttpResponseMessage response,
            Stopwatch stopwatch)
        {
            Logger.Warning(
                $"Event=LicenseManifestReadFinished Status={status} ErrorCode={errorCode} " +
                $"HttpStatus={(int)response.StatusCode} ElapsedMs={stopwatch.ElapsedMilliseconds}",
                ModuleName);
            return LicenseManifestReadResult.Failure(status, errorCode);
        }

        private static LicenseManifestReadResult LogFailure(
            LicenseManifestReadStatus status,
            string errorCode,
            Stopwatch stopwatch)
        {
            Logger.Warning(
                $"Event=LicenseManifestReadFinished Status={status} ErrorCode={errorCode} " +
                $"ElapsedMs={stopwatch.ElapsedMilliseconds}",
                ModuleName);
            return LicenseManifestReadResult.Failure(status, errorCode);
        }

        private sealed class ResponseTooLargeException : Exception
        {
        }
    }
}

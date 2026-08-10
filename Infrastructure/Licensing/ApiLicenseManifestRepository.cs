using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Models.Licensing;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Licensing
{
    public sealed class ApiLicenseManifestRepository : ILicenseManifestRepository
    {
        private readonly IApiSessionService _sessionService;
        private readonly ILicenseSignatureVerifier _signatureVerifier;
        private readonly TimeSpan _timeout;

        public ApiLicenseManifestRepository(IApiSessionService sessionService, ILicenseSignatureVerifier signatureVerifier, TimeSpan? timeout = null)
        {
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
            _signatureVerifier = signatureVerifier ?? throw new ArgumentNullException(nameof(signatureVerifier));
            _timeout = timeout ?? TimeSpan.FromSeconds(15);
        }

        public async Task<LicenseManifestReadResult> ReadAsync(LicenseGrantRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            using var timeoutSource = new CancellationTokenSource(_timeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            try
            {
                using var apiRequest = new HttpRequestMessage(HttpMethod.Get, "api/license/current");
                using HttpResponseMessage response = await _sessionService.SendAuthorizedAsync(apiRequest, linkedSource.Token);
                if (!response.IsSuccessStatusCode) return MapStatus(response.StatusCode);

                ApiLicenseResponse payload;
                try
                {
                    payload = JsonConvert.DeserializeObject<ApiLicenseResponse>(await response.Content.ReadAsStringAsync(linkedSource.Token));
                }
                catch (JsonException)
                {
                    return Failure(LicenseManifestReadStatus.InvalidJson, "MalformedApiLicenseResponse");
                }
                if (payload == null || string.IsNullOrWhiteSpace(payload.GrantBase64) ||
                    string.IsNullOrWhiteSpace(payload.SignatureBase64) || string.IsNullOrWhiteSpace(payload.KeyId))
                    return Failure(LicenseManifestReadStatus.InvalidJson, "IncompleteApiLicenseResponse");

                byte[] grantBytes;
                byte[] rawSignature;
                try
                {
                    grantBytes = Convert.FromBase64String(payload.GrantBase64);
                    rawSignature = Convert.FromBase64String(payload.SignatureBase64);
                }
                catch (FormatException)
                {
                    return Failure(LicenseManifestReadStatus.InvalidJson, "InvalidApiBase64");
                }

                byte[] envelopeBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new LicenseSignatureEnvelope
                {
                    KeyId = payload.KeyId,
                    Algorithm = LicenseSignatureEnvelope.EcdsaP256Sha256Algorithm,
                    Signature = Convert.ToBase64String(rawSignature)
                }));
                LicenseSignatureVerificationResult signature = _signatureVerifier.Verify(grantBytes, envelopeBytes);
                if (!signature.IsValid)
                    return Failure(signature.Status == LicenseSignatureVerificationStatus.UnknownKeyId
                        ? LicenseManifestReadStatus.UnknownKey : LicenseManifestReadStatus.InvalidSignature, signature.ErrorCode);

                LicenseGrant grant;
                try
                {
                    grant = JsonConvert.DeserializeObject<LicenseGrant>(new UTF8Encoding(false, true).GetString(grantBytes));
                }
                catch (Exception ex) when (ex is JsonException || ex is DecoderFallbackException)
                {
                    return Failure(LicenseManifestReadStatus.InvalidJson, "InvalidSignedGrantJson");
                }
                if (grant == null || grant.SchemaVersion != 1)
                    return Failure(LicenseManifestReadStatus.UnsupportedSchema, "UnsupportedSchemaVersion");
                if (LicenseGrantValidator.Validate(grant).Count > 0 ||
                    !string.Equals(grant.ClientId, request.ClientId, StringComparison.Ordinal) ||
                    !string.Equals(grant.DeviceId, request.DeviceId, StringComparison.Ordinal) ||
                    grant.Revision != payload.Revision || grant.IssuedAtUtc != NormalizeServerUtc(payload.IssuedAtUtc) ||
                    grant.ValidUntilUtc != NormalizeServerUtc(payload.ValidUntilUtc))
                    return Failure(LicenseManifestReadStatus.InvalidManifest, "ApiGrantMetadataMismatch");

                return LicenseManifestReadResult.Success(grant, grantBytes, envelopeBytes, response.Headers.Date);
            }
            catch (ApiRequestException ex) { return MapStatus(ex.StatusCode); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { return Failure(LicenseManifestReadStatus.Timeout, "ApiRequestTimeout"); }
            catch (HttpRequestException) { return Failure(LicenseManifestReadStatus.NetworkUnavailable, "ApiNetworkUnavailable"); }
            catch (IOException) { return Failure(LicenseManifestReadStatus.NetworkUnavailable, "ApiResponseReadFailed"); }
        }

        private static LicenseManifestReadResult MapStatus(HttpStatusCode status) => status switch
        {
            HttpStatusCode.NotFound => Failure(LicenseManifestReadStatus.NotFound, "ApiHttp404"),
            HttpStatusCode.Unauthorized => Failure(LicenseManifestReadStatus.Unauthorized, "ApiHttp401"),
            HttpStatusCode.Forbidden => Failure(LicenseManifestReadStatus.Forbidden, "ApiHttp403"),
            HttpStatusCode.Gone => Failure(LicenseManifestReadStatus.Gone, "ApiHttp410"),
            HttpStatusCode.TooManyRequests => Failure(LicenseManifestReadStatus.RateLimited, "ApiHttp429"),
            _ when (int)status >= 500 => Failure(LicenseManifestReadStatus.ServerError, "ApiHttp5xx"),
            _ => Failure(LicenseManifestReadStatus.ServerError, "ApiHttpError")
        };

        private static LicenseManifestReadResult Failure(LicenseManifestReadStatus status, string code) =>
            LicenseManifestReadResult.Failure(status, code);

        // HonestLicenseServer versions backed by SQLite can serialize UTC columns
        // without an explicit Z/offset. DateTimeOffset then assumes the workstation's
        // local offset, even though the contract and property name are explicitly UTC.
        private static DateTimeOffset NormalizeServerUtc(DateTimeOffset value) =>
            value.Offset == TimeSpan.Zero
                ? value
                : new DateTimeOffset(DateTime.SpecifyKind(value.DateTime, DateTimeKind.Utc));
    }
}

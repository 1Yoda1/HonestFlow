using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.Licensing;
using HonestFlow.Models.Licensing;
using Newtonsoft.Json;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class ApiLicenseManifestRepositoryTests
    {
        [Fact]
        public async Task SignedApiGrant_PreservesBytesAndVerifiesEcdsa()
        {
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            const string keyId = "test-key";
            var grant = CreateGrant();
            byte[] grantBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(grant));
            byte[] signature = key.SignData(grantBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            var payload = new ApiLicenseResponse
            {
                GrantBase64 = Convert.ToBase64String(grantBytes), SignatureBase64 = Convert.ToBase64String(signature),
                KeyId = keyId, Revision = grant.Revision, IssuedAtUtc = grant.IssuedAtUtc, ValidUntilUtc = grant.ValidUntilUtc
            };
            var verifier = new EcdsaLicenseSignatureVerifier(new LicensePublicKeyRegistry(
                new System.Collections.Generic.Dictionary<string, string> { [keyId] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) }));
            var repository = new ApiLicenseManifestRepository(
                new StaticSessionService(HttpStatusCode.OK, JsonConvert.SerializeObject(payload)), verifier);

            LicenseManifestReadResult result = await repository.ReadAsync(
                new LicenseGrantRequest(grant.ClientId, grant.DeviceId), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(grantBytes, result.GrantBytes.ToArray());
            Assert.Equal(grant.Revision, result.Grant.Revision);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized, LicenseManifestReadStatus.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden, LicenseManifestReadStatus.Forbidden)]
        [InlineData(HttpStatusCode.NotFound, LicenseManifestReadStatus.NotFound)]
        [InlineData(HttpStatusCode.Gone, LicenseManifestReadStatus.Gone)]
        [InlineData(HttpStatusCode.TooManyRequests, LicenseManifestReadStatus.RateLimited)]
        [InlineData(HttpStatusCode.InternalServerError, LicenseManifestReadStatus.ServerError)]
        public async Task HttpStatus_IsMappedWithoutCacheMasking(HttpStatusCode http, LicenseManifestReadStatus expected)
        {
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var verifier = new EcdsaLicenseSignatureVerifier(new LicensePublicKeyRegistry(new System.Collections.Generic.Dictionary<string, string>()));
            var repository = new ApiLicenseManifestRepository(new StaticSessionService(http, ""), verifier);
            LicenseManifestReadResult result = await repository.ReadAsync(new LicenseGrantRequest("client", "device"), CancellationToken.None);
            Assert.Equal(expected, result.Status);
        }

        private static LicenseGrant CreateGrant() => new()
        {
            SchemaVersion = 1, Revision = 42, ClientId = "client", DeviceId = "device",
            ClientEnabled = true, DeviceEnabled = true, OperatorDevice = false,
            MinHonestFlowVersion = "3.0.0", OfflineGraceHours = 24,
            IssuedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1), ValidUntilUtc = DateTimeOffset.UtcNow.AddDays(1)
        };

        private sealed class StaticSessionService : IApiSessionService
        {
            private readonly HttpStatusCode _status; private readonly string _json;
            public StaticSessionService(HttpStatusCode status, string json) { _status = status; _json = json; }
            public Task<ApiTokenResponse> LoginAsync(string login, string password, string deviceId, string deviceName, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<HttpResponseMessage> SendAuthorizedAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_json, Encoding.UTF8, "application/json") });
        }
    }
}

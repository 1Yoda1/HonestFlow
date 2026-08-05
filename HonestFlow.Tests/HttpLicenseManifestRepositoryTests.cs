using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Licensing;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class HttpLicenseManifestRepositoryTests
    {
        private static readonly LicenseGrantRequest Request = new("client-1", "device-1");

        [Fact]
        public async Task Read_ReturnsOnlyMatchingGrant()
        {
            var repository = CreateRepository(ValidJson);
            LicenseManifestReadResult result = await repository.ReadAsync(Request, CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal("client-1", result.Grant.ClientId);
            Assert.Equal("device-1", result.Grant.DeviceId);
        }

        [Fact]
        public async Task Read_RejectsGrantForAnotherSubject()
        {
            var repository = CreateRepository(ValidJson.Replace("device-1", "device-2"));
            LicenseManifestReadResult result = await repository.ReadAsync(Request, CancellationToken.None);
            Assert.Equal(LicenseManifestReadStatus.InvalidManifest, result.Status);
            Assert.Equal("GrantSubjectMismatch", result.ErrorCode);
        }

        [Fact]
        public async Task Read_RejectsMalformedJson()
        {
            LicenseManifestReadResult result = await CreateRepository("{bad")
                .ReadAsync(Request, CancellationToken.None);
            Assert.Equal(LicenseManifestReadStatus.InvalidJson, result.Status);
        }

        private static HttpLicenseManifestRepository CreateRepository(string json)
        {
            var client = new HttpClient(new Handler(request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.RequestUri.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal)
                        ? "signature"
                        : json,
                    Encoding.UTF8,
                    "application/json")
            }));
            return new HttpLicenseManifestRepository(
                client,
                new LicenseManifestRepositoryOptions
                {
                    ManifestUrl = new Uri("https://licenses.test/grant.json"),
                    SignatureUrl = new Uri("https://licenses.test/grant.json.sig"),
                    RequestTimeout = TimeSpan.FromSeconds(2),
                    MaxResponseBytes = 4096
                },
                new ValidVerifier());
        }

        private const string ValidJson =
            "{\"SchemaVersion\":1,\"Revision\":7,\"ClientId\":\"client-1\",\"DeviceId\":\"device-1\"," +
            "\"ClientEnabled\":true,\"DeviceEnabled\":true,\"MinHonestFlowVersion\":\"3.0.0\"," +
            "\"OfflineGraceHours\":24,\"IssuedAtUtc\":\"2026-08-01T00:00:00Z\"," +
            "\"ValidUntilUtc\":\"2026-09-01T00:00:00Z\",\"Features\":[]}";

        private sealed class Handler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;
            public Handler(Func<HttpRequestMessage, HttpResponseMessage> response) => _response = response;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(_response(request));
        }

        private sealed class ValidVerifier : ILicenseSignatureVerifier
        {
            public LicenseSignatureVerificationResult Verify(ReadOnlyMemory<byte> grantBytes, ReadOnlyMemory<byte> signatureFileBytes) =>
                LicenseSignatureVerificationResult.Valid();
        }
    }
}

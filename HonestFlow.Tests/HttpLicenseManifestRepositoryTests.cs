using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Licensing;
using HonestFlow.Models.Licensing;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class HttpLicenseManifestRepositoryTests
    {
        [Fact]
        public async Task ReadAsync_ReturnsSuccessForSupportedManifest()
        {
            var repository = CreateRepository(_ => JsonResponse(HttpStatusCode.OK, ValidJson));

            LicenseManifestReadResult result = await repository.ReadAsync(CancellationToken.None);

            Assert.Equal(LicenseManifestReadStatus.Success, result.Status);
            Assert.True(result.IsSuccess);
            Assert.Equal(7, result.Manifest.Revision);
        }

        [Fact]
        public async Task ReadAsync_ReturnsNetworkUnavailableForHttpFailure()
        {
            Func<CancellationToken, HttpResponseMessage> failure =
                _ => throw new HttpRequestException("Network unavailable");
            var repository = CreateRepository(failure);

            LicenseManifestReadResult result = await repository.ReadAsync(CancellationToken.None);

            Assert.Equal(LicenseManifestReadStatus.NetworkUnavailable, result.Status);
        }

        [Fact]
        public async Task ReadAsync_ReturnsTimeoutWhenConfiguredTimeoutExpires()
        {
            var repository = CreateRepository(
                async cancellationToken =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return JsonResponse(HttpStatusCode.OK, ValidJson);
                },
                timeout: TimeSpan.FromMilliseconds(30));

            LicenseManifestReadResult result = await repository.ReadAsync(CancellationToken.None);

            Assert.Equal(LicenseManifestReadStatus.Timeout, result.Status);
        }

        [Fact]
        public async Task ReadAsync_PropagatesCallerCancellation()
        {
            var repository = CreateRepository(async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return JsonResponse(HttpStatusCode.OK, ValidJson);
            });
            using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => repository.ReadAsync(cancellationSource.Token));
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound, LicenseManifestReadStatus.NotFound)]
        [InlineData(HttpStatusCode.InternalServerError, LicenseManifestReadStatus.ServerError)]
        public async Task ReadAsync_MapsNonSuccessStatusCodes(
            HttpStatusCode statusCode,
            LicenseManifestReadStatus expectedStatus)
        {
            var repository = CreateRepository(_ => JsonResponse(statusCode, string.Empty));

            LicenseManifestReadResult result = await repository.ReadAsync(CancellationToken.None);

            Assert.Equal(expectedStatus, result.Status);
        }

        [Fact]
        public async Task ReadAsync_ReturnsInvalidJsonForEmptyResponse()
        {
            var repository = CreateRepository(_ => JsonResponse(HttpStatusCode.OK, string.Empty));

            LicenseManifestReadResult result = await repository.ReadAsync(CancellationToken.None);

            Assert.Equal(LicenseManifestReadStatus.InvalidJson, result.Status);
            Assert.Equal("EmptyResponse", result.ErrorCode);
        }

        [Fact]
        public async Task ReadAsync_ReturnsInvalidJsonForMalformedJson()
        {
            var repository = CreateRepository(_ => JsonResponse(HttpStatusCode.OK, "{ invalid"));

            LicenseManifestReadResult result = await repository.ReadAsync(CancellationToken.None);

            Assert.Equal(LicenseManifestReadStatus.InvalidJson, result.Status);
        }

        [Fact]
        public async Task ReadAsync_ReturnsUnsupportedSchema()
        {
            string json = ValidJson.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":2");
            var repository = CreateRepository(_ => JsonResponse(HttpStatusCode.OK, json));

            LicenseManifestReadResult result = await repository.ReadAsync(CancellationToken.None);

            Assert.Equal(LicenseManifestReadStatus.UnsupportedSchema, result.Status);
        }

        [Fact]
        public async Task ReadAsync_RejectsResponseOverConfiguredLimit()
        {
            var repository = CreateRepository(
                _ => JsonResponse(HttpStatusCode.OK, ValidJson),
                maxResponseBytes: 10);

            LicenseManifestReadResult result = await repository.ReadAsync(CancellationToken.None);

            Assert.Equal(LicenseManifestReadStatus.ServerError, result.Status);
            Assert.Equal("ResponseTooLarge", result.ErrorCode);
        }

        private static HttpLicenseManifestRepository CreateRepository(
            Func<CancellationToken, Task<HttpResponseMessage>> responseFactory,
            TimeSpan? timeout = null,
            int maxResponseBytes = 4096)
        {
            var handler = new StubHttpMessageHandler((request, cancellationToken) =>
                request.RequestUri.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal)
                    ? Task.FromResult(JsonResponse(HttpStatusCode.OK, "signature-envelope"))
                    : responseFactory(cancellationToken));
            var client = new HttpClient(handler);
            var options = new LicenseManifestRepositoryOptions
            {
                ManifestUrl = new Uri("https://licenses.example.test/licenses.json"),
                SignatureUrl = new Uri("https://licenses.example.test/licenses.json.sig"),
                RequestTimeout = timeout ?? TimeSpan.FromSeconds(2),
                MaxResponseBytes = maxResponseBytes,
                SupportedSchemaVersion = 1
            };

            return new HttpLicenseManifestRepository(client, options, new ValidSignatureVerifier());
        }

        private static HttpLicenseManifestRepository CreateRepository(
            Func<CancellationToken, HttpResponseMessage> responseFactory,
            TimeSpan? timeout = null,
            int maxResponseBytes = 4096)
        {
            return CreateRepository(
                cancellationToken => Task.FromResult(responseFactory(cancellationToken)),
                timeout,
                maxResponseBytes);
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        private const string ValidJson =
            "{\"SchemaVersion\":1,\"Revision\":7," +
            "\"IssuedAtUtc\":\"2026-07-18T00:00:00Z\"," +
            "\"ValidUntilUtc\":\"2027-07-18T00:00:00Z\",\"Clients\":[]}";

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responseFactory;

            public StubHttpMessageHandler(
                Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
            {
                _responseFactory = responseFactory;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return _responseFactory(request, cancellationToken);
            }
        }

        private sealed class ValidSignatureVerifier : ILicenseSignatureVerifier
        {
            public LicenseSignatureVerificationResult Verify(
                ReadOnlyMemory<byte> manifestBytes,
                ReadOnlyMemory<byte> signatureFileBytes)
            {
                return LicenseSignatureVerificationResult.Valid();
            }
        }
    }
}

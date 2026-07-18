using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Licensing;
using HonestFlow.LicenseSigning;
using HonestFlow.Models.Licensing;
using Newtonsoft.Json;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class YandexPointerLicenseManifestRepositoryTests
    {
        [Fact]
        public async Task Read_ResolvesCurrentPointerAndReturnsVerifiedRevision()
        {
            using Fixture fixture = Fixture.Create();

            LicenseManifestReadResult result = await fixture.Repository.ReadAsync(CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(7, result.Manifest.Revision);
            Assert.Equal(6, fixture.Handler.RequestCount);
        }

        [Fact]
        public async Task Read_RejectsPointerHashMismatch()
        {
            using Fixture fixture = Fixture.Create(invalidManifestHash: true);

            LicenseManifestReadResult result = await fixture.Repository.ReadAsync(CancellationToken.None);

            Assert.Equal(LicenseManifestReadStatus.InvalidJson, result.Status);
            Assert.Equal("PointerHashMismatch", result.ErrorCode);
        }

        private sealed class Fixture : IDisposable
        {
            private readonly ECDsa _key;

            private Fixture(ECDsa key, QueueHandler handler, ILicenseManifestRepository repository)
            {
                _key = key;
                Handler = handler;
                Repository = repository;
            }

            public QueueHandler Handler { get; }
            public ILicenseManifestRepository Repository { get; }

            public static Fixture Create(bool invalidManifestHash = false)
            {
                ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                const string keyId = "test-key";
                var manifest = new LicenseManifest
                {
                    SchemaVersion = 1,
                    Revision = 7,
                    IssuedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                    ValidUntilUtc = DateTimeOffset.UtcNow.AddDays(10),
                    Clients = new List<ClientLicense>()
                };
                byte[] manifestBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(manifest));
                string privatePem = "-----BEGIN PRIVATE KEY-----\n" +
                    Convert.ToBase64String(
                        key.ExportPkcs8PrivateKey(),
                        Base64FormattingOptions.InsertLineBreaks) +
                    "\n-----END PRIVATE KEY-----";
                byte[] signatureBytes = new EcdsaLicenseManifestSigner().CreateSignatureFile(
                    manifestBytes,
                    keyId,
                    privatePem);
                string manifestHash = invalidManifestHash
                    ? Convert.ToBase64String(new byte[32])
                    : Hash(manifestBytes);
                var pointer = new YandexLicensePublicationPointer
                {
                    Revision = 7,
                    VersionPath = "/licenses/versions/revision-00000000000000000007",
                    ManifestSha256 = manifestHash,
                    SignatureSha256 = Hash(signatureBytes),
                    PublishedAtUtc = DateTimeOffset.UtcNow
                };
                byte[] pointerBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(pointer));

                var handler = new QueueHandler(
                    JsonResponse("https://download.test/pointer"),
                    BytesResponse(pointerBytes),
                    JsonResponse("https://download.test/manifest"),
                    JsonResponse("https://download.test/signature"),
                    BytesResponse(manifestBytes),
                    BytesResponse(signatureBytes));
                var client = new HttpClient(handler);
                var verifier = new EcdsaLicenseSignatureVerifier(
                    new LicensePublicKeyRegistry(new Dictionary<string, string>
                    {
                        [keyId] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())
                    }));
                var repository = new YandexPointerLicenseManifestRepository(
                    client,
                    "https://disk.test/public",
                    TimeSpan.FromSeconds(5),
                    1024 * 1024,
                    verifier);
                return new Fixture(key, handler, repository);
            }

            public void Dispose()
            {
                _key.Dispose();
            }

            private static string Hash(byte[] bytes)
            {
                using SHA256 sha = SHA256.Create();
                return Convert.ToBase64String(sha.ComputeHash(bytes));
            }

            private static HttpResponseMessage JsonResponse(string href) =>
                BytesResponse(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { href })));

            private static HttpResponseMessage BytesResponse(byte[] bytes) => new(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
        }

        private sealed class QueueHandler : HttpMessageHandler
        {
            private readonly Queue<HttpResponseMessage> _responses;

            public QueueHandler(params HttpResponseMessage[] responses)
            {
                _responses = new Queue<HttpResponseMessage>(responses);
            }

            public int RequestCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                RequestCount++;
                if (_responses.Count == 0)
                    throw new InvalidOperationException("Unexpected HTTP request: " + request.RequestUri);
                return Task.FromResult(_responses.Dequeue());
            }
        }
    }
}

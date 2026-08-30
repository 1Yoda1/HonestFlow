using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Installation.Planning;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Models.Licensing;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class ServiceInstallationModeTests
    {
        [Fact]
        public async Task EmptyPassword_DoesNotCallServiceAuthorization()
        {
            var client = new StubAccessClient(ServiceInstallationAccessResult.Failed(
                ServiceInstallationAccessStatus.Unavailable, "unexpected"));
            var workflow = new ServiceInstallationAccessWorkflow(client);

            ServiceInstallationAccessResult result = await workflow.AuthorizeAsync(
                " ", "x64", "3.0.0", CancellationToken.None);

            Assert.Equal(ServiceInstallationAccessStatus.InvalidPassword, result.Status);
            Assert.Equal(0, client.Calls);
        }

        [Fact]
        public async Task SuccessfulAuthorization_CreatesMemoryOnlyInstallSession()
        {
            using var http = new HttpClient(new JsonHandler(HttpStatusCode.OK, """
                {
                  "accessToken":"hfi_test_token",
                  "expiresInSeconds":1800,
                  "scope":"installation_only",
                  "components":[
                    {"component":"ESM","version":"2.4.1","fileName":"esm.exe","downloadUrl":"https://example.test/esm"}
                  ]
                }
                """)) { BaseAddress = new Uri("https://api.test/") };
            var client = new ApiServiceInstallationAccessClient(http);

            ServiceInstallationAccessResult result = await client.AuthorizeAsync(
                "secret", "x64", "3.0.0", CancellationToken.None);

            Assert.True(result.IsGranted);
            Assert.Equal("hfi_test_token", result.Session.AccessToken);
            Assert.Single(result.Session.Packages);
            Assert.Equal(InstallationComponent.Esm, result.Session.Packages[0].Component);
            result.Session.Dispose();
            Assert.False(result.Session.IsActive);
            Assert.Null(result.Session.AccessToken);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized, "invalid_service_install_password", ServiceInstallationAccessStatus.InvalidPassword)]
        [InlineData(HttpStatusCode.Forbidden, "service_install_access_disabled", ServiceInstallationAccessStatus.Disabled)]
        [InlineData(HttpStatusCode.ServiceUnavailable, "unavailable", ServiceInstallationAccessStatus.Unavailable)]
        public async Task FailedAuthorization_DoesNotReturnSession(
            HttpStatusCode statusCode,
            string errorCode,
            ServiceInstallationAccessStatus expected)
        {
            using var http = new HttpClient(new JsonHandler(statusCode, $"{{\"code\":\"{errorCode}\"}}"))
            { BaseAddress = new Uri("https://api.test/") };
            var client = new ApiServiceInstallationAccessClient(http);

            ServiceInstallationAccessResult result = await client.AuthorizeAsync(
                "secret", "x64", "3.0.0", CancellationToken.None);

            Assert.Equal(expected, result.Status);
            Assert.Null(result.Session);
        }

        [Fact]
        public void InstallationOnlyGuard_AllowsOnlyInstallOperations_WhileSessionIsActive()
        {
            using var session = Session();
            var guard = new InstallationOnlyOperationGuard(session);

            guard.Demand(LicenseOperation.InstallComponents);
            guard.Demand(LicenseOperation.ReinstallComponents);
            Assert.Throws<InvalidOperationException>(() => guard.Demand(LicenseOperation.ViewPointStatus));
            session.Dispose();
            Assert.Throws<InvalidOperationException>(() => guard.Demand(LicenseOperation.InstallComponents));
        }

        [Fact]
        public void StartupServiceMode_IsSeparateFromNormalAuthRegistrationAndLicensePipeline()
        {
            string xaml = File.ReadAllText(ProjectFile("UI", "StartupWindow.xaml"));
            string startup = File.ReadAllText(ProjectFile("UI", "StartupWindow.xaml.cs"));
            string serviceClient = File.ReadAllText(ProjectFile("Infrastructure", "Api", "ApiServiceInstallationAccessClient.cs"));
            string modeXaml = File.ReadAllText(ProjectFile("UI", "InstallationModeWindow.xaml"));

            Assert.Contains("Только установка", xaml);
            Assert.Contains("ServiceAccessDialog", startup);
            Assert.Contains("new InstallationModeWindow", startup);
            Assert.Contains("api/service/install-access", serviceClient);
            Assert.DoesNotContain("api/auth/login", serviceClient);
            Assert.DoesNotContain("api/device/request", serviceClient);
            Assert.DoesNotContain("api/license/current", serviceClient);
            Assert.DoesNotContain("ApiSessionService", serviceClient);
            Assert.DoesNotContain("DPAPI", serviceClient, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Диагност", modeXaml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Лиценз", modeXaml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Администр", modeXaml, StringComparison.OrdinalIgnoreCase);
        }

        private static ServiceInstallationSession Session() => new(
            "hfi_active", DateTimeOffset.UtcNow.AddMinutes(30), Array.Empty<ServiceInstallationPackage>());

        private static string ProjectFile(params string[] parts) => Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", "..", ".." }.Concat(parts).ToArray()));

        private sealed class StubAccessClient : IServiceInstallationAccessClient
        {
            private readonly ServiceInstallationAccessResult _result;
            public StubAccessClient(ServiceInstallationAccessResult result) => _result = result;
            public int Calls { get; private set; }
            public Task<ServiceInstallationAccessResult> AuthorizeAsync(
                string password, string architecture, string appVersion, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(_result);
            }
        }

        private sealed class JsonHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;
            private readonly string _json;
            public JsonHandler(HttpStatusCode statusCode, string json)
            {
                _statusCode = statusCode;
                _json = json;
            }
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(_json, Encoding.UTF8, "application/json")
                });
        }
    }
}

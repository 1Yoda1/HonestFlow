using System;
using System.IO;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Bootstrap;
using HonestFlow.Application.Core;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.PointStatus;
using HonestFlow.Application.ServiceConnection;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Models;
using HonestFlow.Models.Licensing;
using Xunit;

namespace HonestFlow.Tests;

public sealed class ServiceLmChzConfigurationTests
{
    [Fact]
    public void ServiceRuntime_ExposesCurrentConfigurationSecretsWithoutCopyingThemToClient()
    {
        ServiceRuntimeContext runtime = CreateRuntime("service-chz-token", "id-code");

        Assert.Equal("service-chz-token", runtime.ChzToken);
        Assert.Equal("id-code", runtime.IdentificationCode);
        Assert.Null(runtime.Client.Token);
    }

    [Fact]
    public void ServiceInstallationAndRepair_UseRuntimeChzToken_NotLegacyClientToken()
    {
        string autoFix = File.ReadAllText(ProjectFile("UI", "WpfAutoFixComposition.cs"));
        string mainWindow = File.ReadAllText(ProjectFile("UI", "MainWindow.xaml.cs"));

        Assert.Contains("lmChzToken: chzToken", autoFix);
        Assert.Contains("InitializeLmAsync(chzToken, null, token)", autoFix);
        Assert.Contains("_serviceRuntime?.ChzToken", mainWindow);
        Assert.DoesNotContain("InitializeLmAsync(client.Token", autoFix);
    }

    [Fact]
    public void MissingServiceTokenAndFreeRuntimeAreNonFatal()
    {
        ServiceRuntimeContext runtime = CreateRuntime(null, null);
        string autoFix = File.ReadAllText(ProjectFile("UI", "WpfAutoFixComposition.cs"));

        Assert.Null(runtime.ChzToken);
        Assert.Null(new IPData().Token);
        Assert.Contains("string chzToken = null", autoFix);
    }

    [Fact]
    public void LmRequestLog_RedactsServiceSecrets()
    {
        string sanitized = LmApiClient.SanitizeRequestBodyForLog(
            "{\"token\":\"service-chz-token\",\"identificationCode\":\"id-code\"}");

        Assert.DoesNotContain("service-chz-token", sanitized);
        Assert.DoesNotContain("id-code", sanitized);
        Assert.Contains("[redacted]", sanitized);
    }

    [Theory]
    [InlineData("{\"data\":[{\"ChzToken\":\"secret-value\",\"TOKEN\":\"secret-value\",\"IdentificationCode\":\"secret-value\"}]}")]
    [InlineData("{\"token\":\"secret-value\"")]
    public void LmRequestLog_RedactsNestedSecretsAndMalformedPayloads(string payload)
    {
        Assert.DoesNotContain("secret-value", LmApiClient.SanitizeRequestBodyForLog(payload));
    }

    [Theory]
    [InlineData("{\"message\":\"Rejected secret-value\"}")]
    [InlineData("{\"message\":\"Rejected secret-\\u0076alue\"}")]
    [InlineData("Rejected secret-value")]
    [InlineData("{broken secret-value")]
    public void LmInitResponse_RedactsEchoedSubmittedToken(string response)
    {
        string safe = LmApiClient.SanitizeSubmittedToken(response, "secret-value");
        Assert.DoesNotContain("secret-value", safe);
        Assert.DoesNotContain("secret-\\u0076alue", safe);
        Assert.Contains("[redacted]", safe);
    }

    [Fact]
    public void LmInitResponse_RedactsEscapedToken_AndPreservesOtherErrors()
    {
        const string token = "secret\"\\value";
        string response = Newtonsoft.Json.JsonConvert.SerializeObject(new { message = "Rejected " + token });
        string safe = LmApiClient.SanitizeSubmittedToken(response, token);
        Assert.DoesNotContain("value", safe);
        Assert.Contains("[redacted]", safe);
        Assert.Equal("Connection refused", LmApiClient.SanitizeSubmittedToken("Connection refused", token));
    }

    [Fact]
    public void LmInitResponse_UsesRedactedBodyForLogsAndReturnedResult()
    {
        string source = File.ReadAllText(ProjectFile("Infrastructure", "Api", "LmApiClient.cs"));
        Assert.Contains("LogResponse(\"POST\", url, resp.StatusCode, safeResponseBody", source);
        Assert.Contains("ApiSimpleResponse.Success(resp.StatusCode, safeResponseBody", source);
        Assert.Contains("SanitizeSubmittedToken(errorMessage, submittedToken), safeResponseBody", source);
    }

    private static ServiceRuntimeContext CreateRuntime(string chzToken, string identificationCode)
    {
        var log = new StubLogService();
        var startup = new StartupResult { AuthService = new StubAuthService() };
        var session = new ApplicationStartupSession(
            startup,
            log,
            new SellerAuthenticationWorkflow(startup.AuthService, LicenseObservationSnapshotStore.Instance));
        var configuration = new ApiConfigurationResponse
        {
            Client = new ApiClientConfiguration
            {
                ClientId = "client-1", ChzToken = chzToken, IdentificationCode = identificationCode
            },
            Device = new ApiDeviceConfiguration { DeviceId = "device-1" }
        };
        var entitlement = new LicenseObservationSnapshot
        {
            ClientId = "client-1", DeviceId = "device-1", Decision = LicenseDecision.Allowed,
            Features = new[] { LicenseFeature.Service }
        };
        var versions = new ComponentVersionStatusService(new StubVersionCheckService(), () => null);
        var pointStatus = new PointStatusRefreshService(
            new PointStatusService(false, 0), versions, new PointStatusReportBuilder(), log);
        return new ServiceRuntimeContext(
            new ApplicationStartupController(), session, new IPData { ClientId = "client-1" }, entitlement,
            configuration, pointStatus, versions);
    }

    private static string ProjectFile(params string[] parts)
    {
        string path = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8; depth++)
        {
            string candidate = Path.Combine(path, Path.Combine(parts));
            if (File.Exists(candidate)) return candidate;
            path = Directory.GetParent(path)?.FullName ?? path;
        }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }

    private sealed class StubAuthService : IAuthService
    {
        public System.Collections.Generic.List<IPData> Ips => new();
        public void LoadIpList() { }
        public IPData Authenticate(string password) => null;
    }

    private sealed class StubLogService : ILogService
    {
        public void LogDebug(string message) { }
        public void LogUser(string message, bool isError = false) { }
        public string GetUserLog() => string.Empty;
    }

    private sealed class StubVersionCheckService : IVersionCheckService
    {
        public bool NeedAtolInstall(IPData selectedIP, string expectedVersion) => false;
        public bool NeedEsmInstall(string expectedVersion) => false;
        public bool NeedControllerInstall(string expectedVersion) => false;
        public string GetAtolDriverInfo() => null;
        public string GetAtolDriverInfo(string architecture) => null;
        public string GetEsmVersion() => null;
        public string GetControllerVersion() => null;
    }
}

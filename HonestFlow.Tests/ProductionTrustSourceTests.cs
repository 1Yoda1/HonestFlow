using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using HonestFlow.Application.Bootstrap;
using HonestFlow.Application.Core;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Infrastructure.Downloads;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.UI;
using Xunit;

namespace HonestFlow.Tests;

public sealed class ProductionTrustSourceTests
{
    [Fact]
    public void ProductionStartup_IgnoresUserApiBaseUrlAndLegacyStartupEnvironment()
    {
        using var apiBaseUrl = new EnvironmentVariableScope("HONESTFLOW_API_BASE_URL", "https://attacker.example/");
        using var legacyStartup = new EnvironmentVariableScope("HONESTFLOW_USE_LEGACY_STARTUP", "1");

        var startup = new ApplicationStartupService(new NullLog(), new NullProgress(), new NullDialogs()).Start();

        var auth = Assert.IsType<ApiAuthService>(startup.AuthService);
        var session = Assert.IsType<ApiSessionService>(auth.ApiSessionService);
        HttpClient client = (HttpClient)typeof(ApiSessionService)
            .GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(session)!;

        Assert.Equal(HonestLicenseServerEndpoint.ProductionBaseUrl, client.BaseAddress!.AbsoluteUri);
    }

    [Fact]
    public void ServiceInstallationClient_IgnoresUserApiBaseUrl()
    {
        using var apiBaseUrl = new EnvironmentVariableScope("HONESTFLOW_API_BASE_URL", "https://attacker.example/");
        MethodInfo factory = typeof(StartupWindow).GetMethod(
            "CreateServiceInstallationHttpClient",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        using var client = (HttpClient)factory.Invoke(null, null)!;

        Assert.Equal(HonestLicenseServerEndpoint.ProductionBaseUrl, client.BaseAddress!.AbsoluteUri);
    }

    [Fact]
    public void ProductionYandexDownloadTrust_IgnoresUserEnvironmentAndLocalPointers()
    {
        string keyFile = Path.Combine(AppContext.BaseDirectory, "yandex_public_key.txt");
        string urlFile = Path.Combine(AppContext.BaseDirectory, "yandex_public_url.txt");
        using var keyFileScope = new FileContentScope(keyFile, "https://attacker.example/key");
        using var urlFileScope = new FileContentScope(urlFile, "https://attacker.example/url");
        using var environment = new EnvironmentVariableScope("HONESTFLOW_YANDEX_PUBLIC_KEY", "https://attacker.example/env");

        Assert.Equal(ConfigManager.ProductionYandexPublicKey, ConfigManager.GetYandexPublicKey());

        MethodInfo buildDownloadUrl = typeof(YandexDiskDownloader).GetMethod(
            "BuildPublicDownloadUrl",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        string url = (string)buildDownloadUrl.Invoke(null, new object[] { "/installer.exe", null })!;

        Assert.Contains(Uri.EscapeDataString(ConfigManager.ProductionYandexPublicKey), url, StringComparison.Ordinal);
        Assert.DoesNotContain("attacker.example", url, StringComparison.Ordinal);
    }

    private sealed class NullLog : ILogService
    {
        public void LogUser(string message, bool isError = false) { }
        public void LogDebug(string message) { }
        public string GetUserLog() => string.Empty;
    }

    private sealed class NullProgress : IProgressService
    {
        public void SetProgress(int percent, string stepName) { }
    }

    private sealed class NullDialogs : IUserDialogService
    {
        public void ShowInformation(string message, string title) { }
        public void ShowWarning(string message, string title) { }
        public void ShowError(string message, string title) { }
        public bool Confirm(string message, string title, UserDialogIcon icon = UserDialogIcon.Warning) => false;
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previousValue);
    }

    private sealed class FileContentScope : IDisposable
    {
        private readonly string _path;
        private readonly byte[]? _previousContents;

        public FileContentScope(string path, string contents)
        {
            _path = path;
            _previousContents = File.Exists(path) ? File.ReadAllBytes(path) : null;
            File.WriteAllText(path, contents);
        }

        public void Dispose()
        {
            if (_previousContents is null)
            {
                File.Delete(_path);
                return;
            }

            File.WriteAllBytes(_path, _previousContents);
        }
    }
}

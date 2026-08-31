using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Installation.Planning;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Infrastructure.Downloads;
using HonestFlow.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HonestFlow.Infrastructure.Api
{
    public sealed class ApiServiceInstallationAccessClient : IServiceInstallationAccessClient
    {
        private readonly HttpClient _httpClient;

        public ApiServiceInstallationAccessClient(HttpClient httpClient) =>
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        public async Task<ServiceInstallationAccessResult> AuthorizeAsync(
            string password,
            string architecture,
            string appVersion,
            CancellationToken cancellationToken)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/service/install-access")
                {
                    Content = new StringContent(JsonConvert.SerializeObject(new
                    {
                        password,
                        architecture,
                        appVersion
                    }), Encoding.UTF8, "application/json")
                };
                using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    string errorCode = ParseErrorCode(body);
                    if (response.StatusCode == HttpStatusCode.Unauthorized &&
                        errorCode == "invalid_service_install_password")
                        return ServiceInstallationAccessResult.Failed(
                            ServiceInstallationAccessStatus.InvalidPassword,
                            "Неверный пароль сервисного доступа.");
                    if (response.StatusCode == HttpStatusCode.Forbidden &&
                        errorCode == "service_install_access_disabled")
                        return ServiceInstallationAccessResult.Failed(
                            ServiceInstallationAccessStatus.Disabled,
                            "Сервисный доступ к установке отключён.");
                    return Unavailable();
                }

                ApiServiceInstallAccessResponse payload =
                    JsonConvert.DeserializeObject<ApiServiceInstallAccessResponse>(body);
                if (payload == null || payload.Scope != "installation_only" ||
                    string.IsNullOrWhiteSpace(payload.AccessToken) || payload.ExpiresInSeconds <= 0)
                    return Unavailable();

                ServiceInstallationPackage[] packages = (payload.Components ?? new List<ApiServiceInstallComponent>())
                    .Select(MapPackage)
                    .Where(item => item != null)
                    .ToArray();
                var session = new ServiceInstallationSession(
                    payload.AccessToken,
                    DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresInSeconds),
                    packages);
                return ServiceInstallationAccessResult.Granted(session);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return Unavailable();
            }
        }

        private static ServiceInstallationPackage MapPackage(ApiServiceInstallComponent source)
        {
            if (source == null || !TryComponent(source.Component, out InstallationComponent component))
                return null;
            return new ServiceInstallationPackage
            {
                Component = component,
                Version = source.Version,
                FileName = source.FileName,
                DownloadUrl = source.DownloadUrl,
                Sha256 = source.Sha256,
                SizeBytes = source.SizeBytes,
                Architecture = source.Architecture
            };
        }

        private static bool TryComponent(string value, out InstallationComponent component)
        {
            component = value?.Replace("-", "", StringComparison.OrdinalIgnoreCase)
                .Replace("_", "", StringComparison.OrdinalIgnoreCase).ToUpperInvariant() switch
            {
                "LMMODULE" => InstallationComponent.LmModule,
                "ATOLDRIVER" => InstallationComponent.AtolDriver,
                "ESM" => InstallationComponent.Esm,
                "CONTROLLER" => InstallationComponent.Controller,
                _ => (InstallationComponent)(-1)
            };
            return Enum.IsDefined(typeof(InstallationComponent), component);
        }

        private static string ParseErrorCode(string body)
        {
            try { return JObject.Parse(body)["code"]?.Value<string>(); }
            catch (JsonException) { return null; }
        }

        private static ServiceInstallationAccessResult Unavailable() =>
            ServiceInstallationAccessResult.Failed(
                ServiceInstallationAccessStatus.Unavailable,
                "Не удалось подтвердить сервисный доступ. Проверьте подключение к серверу.");

        private sealed class ApiServiceInstallAccessResponse
        {
            public string AccessToken { get; set; }
            public int ExpiresInSeconds { get; set; }
            public string Scope { get; set; }
            public List<ApiServiceInstallComponent> Components { get; set; }
        }

        private sealed class ApiServiceInstallComponent
        {
            public string Component { get; set; }
            public string Version { get; set; }
            public string FileName { get; set; }
            public string DownloadUrl { get; set; }
            public string Sha256 { get; set; }
            public long? SizeBytes { get; set; }
            public string Architecture { get; set; }
        }
    }

    public sealed class ApiInstallationPackageSource : IInstallationPackageSource
    {
        private readonly HttpClient _httpClient;
        private readonly ServiceInstallationSession _session;
        private readonly VerifiedAssetDownloader _downloader;

        public ApiInstallationPackageSource(
            HttpClient httpClient,
            ServiceInstallationSession session,
            VerifiedAssetDownloader downloader = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _downloader = downloader ?? new VerifiedAssetDownloader();
            Versions = session.CreateVersions();
        }

        public VersionsData Versions { get; }

        public async Task<string> ResolveInstallerAsync(
            InstallationComponent component,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            if (!_session.IsActive)
                throw new InvalidOperationException("Срок сервисного доступа истёк. Войдите снова.");
            ServiceInstallationPackage package = _session.Packages.FirstOrDefault(x => x.Component == component);
            if (package == null)
                throw new InvalidOperationException($"Для компонента {component} на сервере не настроен установочный файл.");
            var asset = new TrustedAsset
            {
                FileName = package.FileName,
                DownloadUrl = package.DownloadUrl,
                Sha256 = package.Sha256,
                SizeBytes = package.SizeBytes
            };
            return await _downloader.GetVerifiedAsync(
                asset,
                AppPaths.InstallerCacheFolder,
                async (request, token) =>
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
                    return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                },
                progress,
                cancellationToken);
        }
    }
}

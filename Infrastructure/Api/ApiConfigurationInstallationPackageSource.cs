using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Installation.Planning;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Infrastructure.Downloads;
using HonestFlow.Models;

namespace HonestFlow.Infrastructure.Api
{
    /// <summary>
    /// Production installer source. It accepts only component metadata from the
    /// current authenticated configuration/current response.
    /// </summary>
    public sealed class ApiConfigurationInstallationPackageSource : IInstallationPackageSource
    {
        private readonly IApiSessionService _session;
        private readonly IReadOnlyDictionary<InstallationComponent, TrustedAsset> _assets;
        private readonly VerifiedAssetDownloader _downloader;
        private readonly string _cacheFolder;

        public ApiConfigurationInstallationPackageSource(
            IApiSessionService session,
            ApiConfigurationResponse configuration,
            VerifiedAssetDownloader downloader = null,
            string cacheFolder = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            if (configuration is null) throw new ArgumentNullException(nameof(configuration));
            _downloader = downloader ?? new VerifiedAssetDownloader();
            _cacheFolder = cacheFolder ?? AppPaths.InstallerCacheFolder;
            Versions = ToVersions(configuration.Components);
            _assets = ToAssets(configuration.Components);
        }

        public VersionsData Versions { get; }

        public async Task<string> ResolveInstallerAsync(
            InstallationComponent component,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            if (!_assets.TryGetValue(component, out TrustedAsset asset))
                throw new InvalidOperationException($"Для компонента {component} нет доверенного описания на сервере.");

            return await _downloader.GetVerifiedAsync(
                asset,
                _cacheFolder,
                (request, token) => _session.SendAuthorizedAsync(request, token),
                progress,
                cancellationToken);
        }

        public static ApiConfigurationInstallationPackageSource CreateRequired(IAuthService authService)
        {
            if (authService is not IApiSessionProvider sessionProvider ||
                sessionProvider.ApiSessionService is null ||
                authService is not IApiConfigurationProvider configurationProvider ||
                configurationProvider.CurrentConfiguration is null)
            {
                throw new InvalidOperationException(
                    "Не удалось получить доверенную конфигурацию компонентов с HonestLicenseServer.");
            }

            return new ApiConfigurationInstallationPackageSource(
                sessionProvider.ApiSessionService,
                configurationProvider.CurrentConfiguration);
        }

        private static IReadOnlyDictionary<InstallationComponent, TrustedAsset> ToAssets(
            IEnumerable<ApiComponentConfiguration> components)
        {
            var result = new Dictionary<InstallationComponent, TrustedAsset>();
            foreach (ApiComponentConfiguration component in components ?? Array.Empty<ApiComponentConfiguration>())
            {
                if (component is null || !TryMapComponent(component.Component, out InstallationComponent installationComponent))
                    continue;
                result[installationComponent] = new TrustedAsset
                {
                    FileName = component.FileName,
                    DownloadUrl = component.DownloadUrl,
                    Sha256 = component.Sha256,
                    SizeBytes = component.SizeBytes
                };
            }
            return result;
        }

        private static VersionsData ToVersions(IEnumerable<ApiComponentConfiguration> components)
        {
            var versions = new VersionsData();
            foreach (ApiComponentConfiguration component in components ?? Array.Empty<ApiComponentConfiguration>())
            {
                if (component is null) continue;
                switch (NormalizeComponentName(component.Component))
                {
                    case "LMMODULE": versions.LmModule = component.EffectiveVersion; break;
                    case "ATOLDRIVER": versions.AtolDriver = component.EffectiveVersion; break;
                    case "ESM": versions.ESM = component.EffectiveVersion; break;
                    case "CONTROLLER": versions.Controller = component.EffectiveVersion; break;
                    case "HONESTFLOW": versions.HonestFlow = component.EffectiveVersion; break;
                }
            }
            return versions;
        }

        private static bool TryMapComponent(string value, out InstallationComponent component)
        {
            component = NormalizeComponentName(value) switch
            {
                "LMMODULE" => InstallationComponent.LmModule,
                "ATOLDRIVER" => InstallationComponent.AtolDriver,
                "ESM" => InstallationComponent.Esm,
                "CONTROLLER" => InstallationComponent.Controller,
                _ => (InstallationComponent)(-1)
            };
            return Enum.IsDefined(typeof(InstallationComponent), component);
        }

        private static string NormalizeComponentName(string value) => value?
            .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .ToUpperInvariant() ?? string.Empty;
    }
}

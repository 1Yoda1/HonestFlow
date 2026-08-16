using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Installation.Planning;
using HonestFlow.Application.Licensing;
using HonestFlow.Models;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.Installation
{
    public enum ServiceInstallationAccessStatus
    {
        Granted,
        InvalidPassword,
        Disabled,
        Unavailable
    }

    public sealed class ServiceInstallationPackage
    {
        public InstallationComponent Component { get; init; }
        public string Version { get; init; }
        public string FileName { get; init; }
        public string DownloadUrl { get; init; }
        public string Sha256 { get; init; }
        public long? SizeBytes { get; init; }
        public string Architecture { get; init; }
    }

    public sealed class ServiceInstallationSession : IDisposable
    {
        public ServiceInstallationSession(
            string accessToken,
            DateTimeOffset expiresAtUtc,
            IReadOnlyList<ServiceInstallationPackage> packages)
        {
            AccessToken = string.IsNullOrWhiteSpace(accessToken)
                ? throw new ArgumentException("Install-only token is required.", nameof(accessToken))
                : accessToken;
            ExpiresAtUtc = expiresAtUtc;
            Packages = packages ?? Array.Empty<ServiceInstallationPackage>();
        }

        public string AccessToken { get; private set; }
        public DateTimeOffset ExpiresAtUtc { get; }
        public IReadOnlyList<ServiceInstallationPackage> Packages { get; }
        public bool IsActive => !string.IsNullOrWhiteSpace(AccessToken) && ExpiresAtUtc > DateTimeOffset.UtcNow;

        public VersionsData CreateVersions() => new()
        {
            LmModule = Version(InstallationComponent.LmModule),
            AtolDriver = Version(InstallationComponent.AtolDriver),
            ESM = Version(InstallationComponent.Esm),
            Controller = Version(InstallationComponent.Controller)
        };

        public void Dispose() => AccessToken = null;

        private string Version(InstallationComponent component)
        {
            foreach (ServiceInstallationPackage package in Packages)
                if (package.Component == component) return package.Version;
            return null;
        }
    }

    public sealed class ServiceInstallationAccessResult
    {
        private ServiceInstallationAccessResult(
            ServiceInstallationAccessStatus status,
            ServiceInstallationSession session,
            string message)
        {
            Status = status;
            Session = session;
            Message = message ?? string.Empty;
        }

        public ServiceInstallationAccessStatus Status { get; }
        public ServiceInstallationSession Session { get; }
        public string Message { get; }
        public bool IsGranted => Status == ServiceInstallationAccessStatus.Granted && Session?.IsActive == true;

        public static ServiceInstallationAccessResult Granted(ServiceInstallationSession session) =>
            new(ServiceInstallationAccessStatus.Granted, session, string.Empty);
        public static ServiceInstallationAccessResult Failed(ServiceInstallationAccessStatus status, string message) =>
            new(status, null, message);
    }

    public interface IServiceInstallationAccessClient
    {
        Task<ServiceInstallationAccessResult> AuthorizeAsync(
            string password,
            string architecture,
            string appVersion,
            CancellationToken cancellationToken);
    }

    public sealed class ServiceInstallationAccessWorkflow
    {
        private readonly IServiceInstallationAccessClient _client;

        public ServiceInstallationAccessWorkflow(IServiceInstallationAccessClient client) =>
            _client = client ?? throw new ArgumentNullException(nameof(client));

        public Task<ServiceInstallationAccessResult> AuthorizeAsync(
            string password,
            string architecture,
            string appVersion,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(password))
                return Task.FromResult(ServiceInstallationAccessResult.Failed(
                    ServiceInstallationAccessStatus.InvalidPassword,
                    "Введите пароль сервисного доступа."));
            return _client.AuthorizeAsync(password, architecture, appVersion, cancellationToken);
        }
    }

    public interface IInstallationPackageSource
    {
        VersionsData Versions { get; }
        Task<string> ResolveInstallerAsync(
            InstallationComponent component,
            IProgress<int> progress,
            CancellationToken cancellationToken);
    }

    public sealed class InstallationOnlyOperationGuard : ILicenseOperationGuard
    {
        private readonly ServiceInstallationSession _session;

        public InstallationOnlyOperationGuard(ServiceInstallationSession session) =>
            _session = session ?? throw new ArgumentNullException(nameof(session));

        public void Demand(LicenseOperation operation)
        {
            if (!_session.IsActive)
                throw new InvalidOperationException("Срок сервисного доступа истёк. Войдите снова.");
            if (operation is not (LicenseOperation.InstallComponents or LicenseOperation.ReinstallComponents))
                throw new InvalidOperationException("Сервисный доступ разрешает только установку компонентов.");
        }
    }
}

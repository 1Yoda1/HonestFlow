using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Core;
using HonestFlow.Models;

namespace HonestFlow.Application.RemoteAccess
{
    public sealed class RuDesktopWorkflow
    {
        private readonly RuDesktopService _service;
        private readonly IRuDesktopInstaller _installer;
        private readonly ILogService _log;

        public RuDesktopWorkflow(
            RuDesktopService service,
            IRuDesktopInstaller installer,
            ILogService log)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _installer = installer ?? throw new ArgumentNullException(nameof(installer));
            _log = log;
        }

        public RuDesktopPackage GetInstallationPackage() =>
            RuDesktopInstaller.GetPackageForCurrentOperatingSystem();

        public async Task<RuDesktopWorkflowInstallResult> InstallAsync(
            IProgress<RuDesktopInstallProgress> progress,
            CancellationToken cancellationToken)
        {
            RuDesktopInstallResult install = await _installer.InstallAsync(progress);
            if (!install.IsSuccess)
                return new RuDesktopWorkflowInstallResult(install, null);

            cancellationToken.ThrowIfCancellationRequested();
            _service.ResetLocalConfigurationAfterInstallation();
            RuDesktopStatus status = await _service.WaitForReady(
                timeout: TimeSpan.FromSeconds(15),
                pollInterval: TimeSpan.FromSeconds(1));
            return new RuDesktopWorkflowInstallResult(install, status);
        }

        public async Task<RuDesktopHelpReadiness> GetHelpReadinessAsync()
        {
            RuDesktopStatus status = await _service.GetStatus();
            return new RuDesktopHelpReadiness(
                status.IsInstalled,
                status.PasswordConfiguredByHonestFlow,
                status);
        }

        public Task<RuDesktopSetupResult> ConfigurePasswordAsync(IPData client)
        {
            if (string.IsNullOrWhiteSpace(client?.RuDesktop?.Password))
                throw new InvalidOperationException("В карточке клиента не указан пароль RuDesktop.");
            return _service.ConfigurePermanentPassword(client.RuDesktop.Password);
        }

        public async Task<string> ResolveHelpIdAsync()
        {
            try
            {
                string current = await _service.GetId();
                if (!string.IsNullOrWhiteSpace(current))
                    return current;
            }
            catch (Exception ex)
            {
                _log?.LogDebug($"RuDesktop ID для заявки помощи не получен: {ex.Message}");
            }

            return _service.GetLastKnownId();
        }

        public LastAuthorizedClientState GetLastAuthorizedClient() =>
            _service.GetLastAuthorizedClient();

        public void SaveLastAuthorizedClient(IPData client) =>
            _service.SaveLastAuthorizedClient(client);

        public Task<bool> NeedsInitialPasswordSetupAsync() =>
            _service.NeedsInitialPasswordSetup();

        public bool ShouldOfferPasswordSetup(IPData client) =>
            _service.ShouldOfferPasswordSetup(client);

        public Task<string> GetIdAsync() => _service.GetId();

    }

    public sealed class RuDesktopWorkflowInstallResult
    {
        public RuDesktopWorkflowInstallResult(RuDesktopInstallResult installResult, RuDesktopStatus status)
        {
            InstallResult = installResult;
            Status = status;
        }
        public RuDesktopInstallResult InstallResult { get; }
        public RuDesktopStatus Status { get; }
    }

    public sealed class RuDesktopHelpReadiness
    {
        public RuDesktopHelpReadiness(bool isInstalled, bool passwordConfigured, RuDesktopStatus status)
        {
            IsInstalled = isInstalled;
            PasswordConfigured = passwordConfigured;
            Status = status;
        }
        public bool IsInstalled { get; }
        public bool PasswordConfigured { get; }
        public RuDesktopStatus Status { get; }
        public bool IsReady => IsInstalled && PasswordConfigured;
    }
}

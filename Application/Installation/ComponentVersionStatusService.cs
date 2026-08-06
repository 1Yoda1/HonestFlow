using System;
using HonestFlow.Application.Core;
using HonestFlow.Application.Lm;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Models;

namespace HonestFlow.Application.Installation
{
    public sealed class ComponentVersionStatusService
    {
        private readonly IVersionCheckService _versionChecker;
        private readonly Func<string> _installedLmVersion;

        public ComponentVersionStatusService(ILogService logService)
            : this(
                new VersionCheckService(logService),
                () => new LmValidationService(logService).GetInstalledPhysicalVersion())
        {
        }

        public ComponentVersionStatusService(
            IVersionCheckService versionChecker,
            Func<string> installedLmVersion)
        {
            _versionChecker = versionChecker ?? throw new ArgumentNullException(nameof(versionChecker));
            _installedLmVersion = installedLmVersion ?? throw new ArgumentNullException(nameof(installedLmVersion));
        }

        public ComponentVersionStatus[] GetStatuses(
            IPData selectedClient,
            VersionsData configuredVersions = null)
        {
            VersionsData configured = configuredVersions ?? ConfigManager.LoadVersions();
            VersionsData clientVersions = selectedClient?.Versions;
            var expected = new VersionsData
            {
                LmModule = FirstConfigured(clientVersions?.LmModule, configured?.LmModule),
                AtolDriver = FirstConfigured(clientVersions?.AtolDriver, configured?.AtolDriver),
                ESM = FirstConfigured(clientVersions?.ESM, configured?.ESM),
                Controller = FirstConfigured(clientVersions?.Controller, configured?.Controller)
            };

            string lmVersion = _installedLmVersion();
            string atolVersion = _versionChecker.GetAtolDriverInfo();
            string esmVersion = _versionChecker.GetEsmVersion();
            string controllerVersion = _versionChecker.GetControllerVersion();

            return new[]
            {
                ComponentVersionStatus.Create(
                    "ЛМ ЧЗ",
                    lmVersion,
                    expected.LmModule,
                    CompareLmVersion(lmVersion, expected.LmModule)),
                ComponentVersionStatus.Create(
                    "Драйвер ККТ",
                    atolVersion,
                    expected.AtolDriver,
                    HasExpected(expected.AtolDriver)
                        ? _versionChecker.NeedAtolInstall(selectedClient, expected.AtolDriver)
                        : null),
                ComponentVersionStatus.Create(
                    "ЕСМ",
                    esmVersion,
                    expected.ESM,
                    HasExpected(expected.ESM) ? _versionChecker.NeedEsmInstall(expected.ESM) : null),
                ComponentVersionStatus.Create(
                    "Контроллер",
                    controllerVersion,
                    expected.Controller,
                    HasExpected(expected.Controller) ? _versionChecker.NeedControllerInstall(expected.Controller) : null)
            };
        }

        private static string FirstConfigured(string clientValue, string defaultValue) =>
            !string.IsNullOrWhiteSpace(clientValue) ? clientValue.Trim() : defaultValue?.Trim();

        private static bool HasExpected(string version) => !string.IsNullOrWhiteSpace(version);

        private static bool? CompareLmVersion(string installed, string expected)
        {
            if (!HasExpected(expected))
                return null;

            return !string.Equals(
                NormalizeLmVersion(installed),
                NormalizeLmVersion(expected),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeLmVersion(string version) =>
            string.IsNullOrWhiteSpace(version) ? null : version.Trim().Split('-', 2)[0];
    }
}

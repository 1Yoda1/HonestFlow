using System;
using System.Collections.Generic;
using System.Linq;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Installation.Planning;

namespace HonestFlow.Application.PointStatus;

public static class AutoFixInstallationSelection
{
    public static IReadOnlyCollection<InstallationComponent> From(DiagnosticsSnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

        var components = new HashSet<InstallationComponent>();
        foreach (ComponentVersionStatus version in snapshot.Versions ?? Array.Empty<ComponentVersionStatus>())
        {
            if (version.State is not (ComponentVersionState.NotInstalled or
                ComponentVersionState.BelowMinimum or ComponentVersionState.UpdateRequired))
                continue;

            AddVersionComponent(components, version.ComponentName);
        }

        foreach (DiagnosticIssue issue in snapshot.Issues ?? Array.Empty<DiagnosticIssue>())
        {
            if (issue.SuggestedFix != DiagnosticFixKey.RunSmartInstallation)
                continue;

            switch (issue.Code)
            {
                case DiagnosticIssueCode.LM_NOT_INSTALLED:
                    components.Add(InstallationComponent.LmModule);
                    break;
                case DiagnosticIssueCode.ESM_NOT_INSTALLED:
                case DiagnosticIssueCode.ESM_SERVICE_MISSING:
                    components.Add(InstallationComponent.Esm);
                    break;
                case DiagnosticIssueCode.LM_CONTROLLER_DISCONNECTED:
                    components.Add(InstallationComponent.Controller);
                    break;
                case DiagnosticIssueCode.ATOL_DRIVER_ARCHITECTURE_MISMATCH:
                case DiagnosticIssueCode.ATOL_DRIVER_MISSING:
                case DiagnosticIssueCode.ATOL_DRIVER_TOO_OLD:
                case DiagnosticIssueCode.KKT_SERVICE_MISSING:
                    components.Add(InstallationComponent.AtolDriver);
                    break;
            }
        }

        return components.ToArray();
    }

    private static void AddVersionComponent(ISet<InstallationComponent> components, string name)
    {
        if (string.Equals(name, "ЛМ ЧЗ", StringComparison.OrdinalIgnoreCase))
            components.Add(InstallationComponent.LmModule);
        else if (string.Equals(name, "Драйвер ККТ", StringComparison.OrdinalIgnoreCase))
            components.Add(InstallationComponent.AtolDriver);
        else if (string.Equals(name, "ЕСМ", StringComparison.OrdinalIgnoreCase))
            components.Add(InstallationComponent.Esm);
        else if (string.Equals(name, "Контроллер", StringComparison.OrdinalIgnoreCase))
            components.Add(InstallationComponent.Controller);
    }
}

using System;

namespace HonestFlow.Application.Installation
{
    public enum ComponentVersionState
    {
        Current,
        UpdateRequired,
        NotInstalled,
        Unknown
    }

    public sealed class ComponentVersionStatus
    {
        public ComponentVersionStatus(
            string componentName,
            string installedVersion,
            string expectedVersion,
            ComponentVersionState state)
        {
            ComponentName = componentName;
            InstalledVersion = installedVersion;
            ExpectedVersion = expectedVersion;
            State = state;
        }

        public string ComponentName { get; }
        public string InstalledVersion { get; }
        public string ExpectedVersion { get; }
        public ComponentVersionState State { get; }

        public string StateText => State switch
        {
            ComponentVersionState.Current => "Актуально",
            ComponentVersionState.UpdateRequired => "Нужно обновить",
            ComponentVersionState.NotInstalled => "Не установлено",
            _ => "Не удалось определить"
        };

        public static ComponentVersionStatus Create(
            string componentName,
            string installedVersion,
            string expectedVersion,
            bool? updateRequired)
        {
            string installed = Normalize(installedVersion);
            string expected = Normalize(expectedVersion);

            if (IsUnknown(installedVersion))
                return new ComponentVersionStatus(componentName, null, expected, ComponentVersionState.Unknown);

            if (string.IsNullOrWhiteSpace(installed))
                return new ComponentVersionStatus(componentName, null, expected, ComponentVersionState.NotInstalled);

            if (string.IsNullOrWhiteSpace(expected) || updateRequired == null)
                return new ComponentVersionStatus(componentName, installed, expected, ComponentVersionState.Unknown);

            return new ComponentVersionStatus(
                componentName,
                installed,
                expected,
                updateRequired.Value ? ComponentVersionState.UpdateRequired : ComponentVersionState.Current);
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string normalized = value.Trim();
            if (normalized.Equals("не установлен", StringComparison.OrdinalIgnoreCase))
                return null;

            return normalized;
        }

        private static bool IsUnknown(string value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Trim().Equals("версия не определена", StringComparison.OrdinalIgnoreCase);
    }
}

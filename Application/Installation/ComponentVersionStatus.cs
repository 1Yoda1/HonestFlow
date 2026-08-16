using System;

namespace HonestFlow.Application.Installation
{
    public enum ComponentVersionState
    {
        Current,
        UpdateRequired,
        BelowMinimum,
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
            : this(componentName, installedVersion, expectedVersion, null, state)
        {
        }

        public ComponentVersionStatus(
            string componentName,
            string installedVersion,
            string targetVersion,
            string minimumSupportedVersion,
            ComponentVersionState state)
        {
            ComponentName = componentName;
            InstalledVersion = installedVersion;
            TargetVersion = targetVersion;
            MinimumSupportedVersion = minimumSupportedVersion;
            State = state;
        }

        public string ComponentName { get; }
        public string InstalledVersion { get; }
        public string TargetVersion { get; }
        public string ExpectedVersion => TargetVersion;
        public string MinimumSupportedVersion { get; }
        public ComponentVersionState State { get; }

        public string StateText => State switch
        {
            ComponentVersionState.Current => "Актуально",
            ComponentVersionState.UpdateRequired => "Нужно обновить",
            ComponentVersionState.BelowMinimum => "Версия не поддерживается",
            ComponentVersionState.NotInstalled => "Не установлено",
            _ => "Не удалось определить"
        };

        public static ComponentVersionStatus Create(
            string componentName,
            string installedVersion,
            string expectedVersion,
            bool? updateRequired,
            string minimumSupportedVersion = null)
        {
            string installed = Normalize(installedVersion);
            string expected = Normalize(expectedVersion);
            string minimum = Normalize(minimumSupportedVersion);

            if (IsUnknown(installedVersion))
                return new ComponentVersionStatus(componentName, null, expected, minimum, ComponentVersionState.Unknown);

            if (string.IsNullOrWhiteSpace(installed))
                return new ComponentVersionStatus(componentName, null, expected, minimum, ComponentVersionState.NotInstalled);

            if (IsBelowMinimum(installed, minimum))
                return new ComponentVersionStatus(componentName, installed, expected, minimum, ComponentVersionState.BelowMinimum);

            if (string.IsNullOrWhiteSpace(expected) || updateRequired == null)
                return new ComponentVersionStatus(componentName, installed, expected, minimum, ComponentVersionState.Unknown);

            return new ComponentVersionStatus(
                componentName,
                installed,
                expected,
                minimum,
                updateRequired.Value ? ComponentVersionState.UpdateRequired : ComponentVersionState.Current);
        }

        private static bool IsBelowMinimum(string installed, string minimum)
        {
            if (string.IsNullOrWhiteSpace(minimum)) return false;
            return TryParseVersion(installed, out Version installedVersion) &&
                   TryParseVersion(minimum, out Version minimumVersion) &&
                   installedVersion < minimumVersion;
        }

        private static bool TryParseVersion(string value, out Version version)
        {
            string token = value?.Trim().Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries)[0];
            return Version.TryParse(token, out version);
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

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace HonestFlow.Models.Licensing
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum LicenseFeature
    {
        [Description("Просмотр состояния и ремонт точки")]
        ViewAndRepair,

        [Description("Установка и обслуживание точки")]
        InstallAndMaintenance,

        [Description("HonestFlow Service")]
        Service
    }

    public enum LicenseOperation
    {
        ViewPointStatus,
        CollectDiagnostics,
        SendDiagnostics,
        RequestHelp,
        InstallComponents,
        ReinstallComponents,
        RestoreLmDatabase,
        ManageServices,
        RecoverLmServices,
        InitializeLm,
        InstallRuDesktop,
        ConfigureRuDesktop,
        AutoFix,
        RegisterTsPiot,
        BootstrapKkt,
        AutomateRuDesktop,
        OpenLocalTools
    }

    public static class LicenseFeatureCatalog
    {
        public static IReadOnlyList<LicenseFeature> ConfigurableFeatures { get; } =
            Array.AsReadOnly(new[]
            {
                LicenseFeature.ViewAndRepair,
                LicenseFeature.InstallAndMaintenance,
                LicenseFeature.Service
            });

        public static string GetDisplayName(LicenseFeature feature)
        {
            FieldInfo field = typeof(LicenseFeature).GetField(feature.ToString());
            return field?.GetCustomAttribute<DescriptionAttribute>()?.Description
                ?? feature.ToString();
        }
    }
}

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

        [Description("Диагностика (старый набор прав)")]
        Diagnostics,

        [Description("Отправка логов (старый набор прав)")]
        SendLogs,

        [Description("Установка (старый набор прав)")]
        Install,

        [Description("Ремонт (старый набор прав)")]
        Repair,

        [Description("Автоматическое исправление (старый набор прав)")]
        AutoFix,

        [Description("Ручные инструменты (старый набор прав)")]
        ManualTools,

        [Description("Просмотр состояния точки")]
        ViewPointStatus,

        [Description("Сбор диагностики")]
        CollectDiagnostics,

        [Description("Отправка диагностики")]
        SendDiagnostics,

        [Description("Запрос помощи")]
        RequestHelp,

        [Description("Установка компонентов")]
        InstallComponents,

        [Description("Переустановка компонентов")]
        ReinstallComponents,

        [Description("Восстановление базы ЛМ ЧЗ")]
        RestoreLmDatabase,

        [Description("Управление службами")]
        ManageServices,

        [Description("Восстановление служб ЛМ ЧЗ")]
        RecoverLmServices,

        [Description("Инициализация ЛМ ЧЗ")]
        InitializeLm,

        [Description("Установка и переустановка RuDesktop")]
        InstallRuDesktop,

        [Description("Настройка постоянного пароля RuDesktop")]
        ConfigureRuDesktop,

        [Description("Запуск драйвера ККТ и ЕСМ")]
        OpenLocalTools
    }

    public static class LicenseFeatureCatalog
    {
        public static IReadOnlyList<LicenseFeature> ConfigurableFeatures { get; } =
            Array.AsReadOnly(new[]
            {
                LicenseFeature.ViewAndRepair,
                LicenseFeature.InstallAndMaintenance
            });

        public static string GetDisplayName(LicenseFeature feature)
        {
            FieldInfo field = typeof(LicenseFeature).GetField(feature.ToString());
            return field?.GetCustomAttribute<DescriptionAttribute>()?.Description
                ?? feature.ToString();
        }
    }
}

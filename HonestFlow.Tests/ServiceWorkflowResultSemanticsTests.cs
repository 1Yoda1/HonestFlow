using System;
using System.IO;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Lm;
using Xunit;

namespace HonestFlow.Tests;

public sealed class ServiceWorkflowResultSemanticsTests
{
    [Fact]
    public void UpdateAll_RequestsRegistrationChoiceBeforeStartingAnOperation()
    {
        string source = ReadProjectFile("UI", "MainWindow.xaml.cs");
        string updateAll = Segment(source, "private async Task InstallAllAsync()", "private async Task RunInstallationOperationAsync");

        int prompt = updateAll.IndexOf("await AskTsPiotRegistrationAsync()", StringComparison.Ordinal);
        int operation = updateAll.IndexOf("RunInstallationOperationAsync", StringComparison.Ordinal);
        Assert.True(prompt >= 0 && prompt < operation);
        Assert.Contains("if (registrationChoice == TsPiotRegistrationChoice.Cancel)", updateAll);
        Assert.Contains("workflow.InstallAsync(_client, cancellationToken, options)", updateAll);
        Assert.Contains("workflow.InstallAndRegisterTsPiotAsync(_client, cancellationToken, options)", updateAll);
        Assert.Contains("Компоненты обновлены.", updateAll);
    }

    [Theory]
    [InlineData(TsPiotRegistrationStatus.Success, "ТС ПИоТ автоматически зарегистрирован")]
    [InlineData(TsPiotRegistrationStatus.EsmUnavailable, "ЕСМ недоступен")]
    [InlineData(TsPiotRegistrationStatus.KktNotDetected, "ККТ не обнаружена")]
    [InlineData(TsPiotRegistrationStatus.RegistrationFailed, "завершилась ошибкой")]
    public void RegistrationResults_HaveSpecificHumanReadablePresentation(
        TsPiotRegistrationStatus status,
        string expectedMessage)
    {
        string source = ReadProjectFile("UI", "MainWindow.xaml.cs");
        string formatter = Segment(source, "private static string FormatInstallationCompletionStatus", "private async Task RefreshAfterComponentOperationAsync");

        Assert.Contains(status.ToString(), formatter);
        Assert.Contains(expectedMessage, formatter);
        Assert.DoesNotContain("не выполнено. Проверьте журнал", formatter);
    }

    [Fact]
    public void LmRestoreResult_PreservesKnownReasonsAndUsesSafeUnknownFallback()
    {
        LmDatabaseRestoreResult backup = LmDatabaseRestoreResult.BackupNotFound(
            "Резервная база ЛМ ЧЗ для ИНН 1701******89 не найдена.");
        LmDatabaseRestoreResult installer = LmDatabaseRestoreResult.InstallerUnavailable(
            "Не найден установщик ЛМ ЧЗ для восстановления базы.");
        LmDatabaseRestoreResult download = LmDatabaseRestoreResult.DownloadFailed();
        LmDatabaseRestoreResult success = LmDatabaseRestoreResult.Success();
        LmDatabaseRestoreResult unknown = LmDatabaseRestoreResult.Failed();

        Assert.Equal(LmDatabaseRestoreStatus.BackupNotFound, backup.Status);
        Assert.Equal("Резервная база ЛМ ЧЗ для ИНН 1701******89 не найдена.", backup.UserMessage);
        Assert.Equal(LmDatabaseRestoreStatus.InstallerUnavailable, installer.Status);
        Assert.Equal(LmDatabaseRestoreStatus.DownloadFailed, download.Status);
        Assert.Contains("Не удалось получить резервную базу", download.UserMessage);
        Assert.True(success.IsSuccess);
        Assert.Equal("База ЛМ ЧЗ восстановлена.", success.UserMessage);
        Assert.False(unknown.IsSuccess);
        Assert.Contains("Подробности записаны в журнал", unknown.UserMessage);
    }

    [Fact]
    public void PartialInstallationResult_PresentationPreservesInstalledState()
    {
        var format = typeof(HonestFlow.UI.MainWindow).GetMethod("FormatInstallationCompletionStatus",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        foreach (var result in new[]
        {
            ComponentInstallationCompletionResult.PostInstallationCancelled(),
            ComponentInstallationCompletionResult.PostInstallationDenied(),
            ComponentInstallationCompletionResult.PostInstallationFailed()
        })
        {
            string message = (string)format.Invoke(null, new object[] { result });
            Assert.Equal(result.PostInstallationUserMessage, message);
            Assert.StartsWith("Компоненты установлены.", message);
        }
    }

    [Fact]
    public void LmRestore_MainWindowUsesStructuredUserMessage()
    {
        string source = ReadProjectFile("UI", "MainWindow.xaml.cs");
        string restore = Segment(source, "private async Task RestoreLmDatabaseAsync", "private async Task RefreshLicenseAsync");

        Assert.Contains("RestoreWithResult", restore);
        Assert.Contains("restoreResult?.UserMessage", restore);
        Assert.DoesNotContain("Восстановление ЛМ ЧЗ не завершено. Проверьте журнал.", restore);
    }

    [Fact]
    public void LmValidation_InitializationIsInProgress_NotCompleted()
    {
        string source = ReadProjectFile("Application", "Lm", "LmValidationService.cs");
        string branch = Segment(source, "else if (result.RuntimeStatus == \"initialization\")", "else");

        Assert.Contains("инициализируется", branch);
        Assert.DoesNotContain("инициализирован)", branch);
    }

    private static string Segment(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Marker not found: {startMarker}");
        Assert.True(end > start, $"Marker not found after start: {endMarker}");
        return source.Substring(start, end - start);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        string path = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8; depth++)
        {
            string candidate = Path.Combine(path, Path.Combine(parts));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            path = Directory.GetParent(path)?.FullName ?? path;
        }

        throw new FileNotFoundException("Project file not found", Path.Combine(parts));
    }
}

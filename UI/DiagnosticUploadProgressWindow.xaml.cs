using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace HonestFlow.UI;

public partial class DiagnosticUploadProgressWindow : Window
{
    private static readonly Brush Pending = new SolidColorBrush(Color.FromRgb(89, 101, 121));
    private static readonly Brush Active = new SolidColorBrush(Color.FromRgb(35, 109, 184));
    private static readonly Brush Completed = new SolidColorBrush(Color.FromRgb(45, 140, 98));
    private static readonly Brush Failed = new SolidColorBrush(Color.FromRgb(190, 61, 61));
    private readonly bool _sending;
    private readonly DispatcherTimer _autoCloseTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private string? _archivePath;
    private bool _terminal;

    public DiagnosticUploadProgressWindow(bool sending)
    {
        InitializeComponent();
        _sending = sending;
        if (!_sending)
        {
            Title = "HonestFlow — Сбор отчёта";
            TitleText.Text = "Формируем отчёт диагностики";
            DescriptionText.Text = "Пожалуйста, не закрывайте HonestFlow до создания архива.";
            UploadStep.Visibility = Visibility.Collapsed;
            VerifyStep.Visibility = Visibility.Collapsed;
        }
        ActivateStep(BuildStep, "Формируем отчёт");
        StatusText.Text = "Подготавливаем данные диагностики…";
        _autoCloseTimer.Tick += AutoCloseTimer_Tick;
    }

    public void ReportBuildStarted()
    {
        EnsureDispatcher(() =>
        {
            Progress.Value = 5;
            ActivateStep(BuildStep, "Формируем отчёт");
            StatusText.Text = "Собираем журналы и создаём архив…";
        });
    }

    public void ReportUploadProgress(int percent, string message)
    {
        EnsureDispatcher(() =>
        {
            Progress.Value = Math.Clamp(percent, 10, 95);
            if (percent >= 90)
            {
                CompleteStep(BuildStep, "Отчёт сформирован");
                CompleteStep(UploadStep, "Отчёт отправлен");
                ActivateStep(VerifyStep, "Проверяем доставку");
            }
            else
            {
                CompleteStep(BuildStep, "Отчёт сформирован");
                ActivateStep(UploadStep, "Отправляем в поддержку");
            }
            StatusText.Text = string.IsNullOrWhiteSpace(message) ? "Отправляем отчёт…" : message + "…";
        });
    }

    public void CompleteUpload(DateTime expiresAtUtc)
    {
        EnsureDispatcher(() =>
        {
            CompleteStep(BuildStep, "Отчёт сформирован");
            CompleteStep(UploadStep, "Отчёт отправлен");
            CompleteStep(VerifyStep, "Доставка подтверждена");
            CompleteStep(ResultStep, "Готово");
            Progress.Value = 100;
            TitleText.Text = "Отчёт отправлен";
            DescriptionText.Text = "Поддержка получила диагностику.";
            StatusText.Text = $"Архив будет автоматически удалён {expiresAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}.";
            Finish(autoClose: true);
        });
    }

    public void CompleteLocalArchive(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        EnsureDispatcher(() =>
        {
            _archivePath = archivePath;
            CompleteStep(BuildStep, "Отчёт сформирован");
            CompleteStep(ResultStep, "Архив готов");
            Progress.Value = 100;
            TitleText.Text = "Архив диагностики готов";
            DescriptionText.Text = "Архив сохранён на этом компьютере.";
            StatusText.Text = "Вы можете открыть архив или закрыть это окно.";
            OpenArchiveButton.Visibility = Visibility.Visible;
            Finish(autoClose: false);
        });
    }

    public void Fail(string message)
    {
        EnsureDispatcher(() =>
        {
            TitleText.Text = "Не удалось отправить отчёт";
            DescriptionText.Text = "Отчёт не был подтверждён поддержкой.";
            ResultStep.Text = "✕  Отправка не завершена";
            ResultStep.Foreground = Failed;
            StatusText.Text = message;
            Finish(autoClose: false);
        });
    }

    private void Finish(bool autoClose)
    {
        _terminal = true;
        CloseButton.IsEnabled = true;
        if (autoClose) _autoCloseTimer.Start();
    }

    private void ActivateStep(TextBlock step, string text)
    {
        step.Text = "●  " + text;
        step.Foreground = Active;
    }

    private static void CompleteStep(TextBlock step, string text)
    {
        step.Text = "✓  " + text;
        step.Foreground = Completed;
    }

    private void EnsureDispatcher(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.BeginInvoke(action);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenArchiveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_archivePath)) return;
        try
        {
            Process.Start(new ProcessStartInfo(_archivePath) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            StatusText.Text = "Не удалось открыть архив: " + exception.Message;
        }
    }

    private void AutoCloseTimer_Tick(object? sender, EventArgs e)
    {
        _autoCloseTimer.Stop();
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_terminal)
        {
            e.Cancel = true;
            StatusText.Text = "Отправка ещё выполняется. Дождитесь результата.";
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer.Stop();
        base.OnClosed(e);
    }
}

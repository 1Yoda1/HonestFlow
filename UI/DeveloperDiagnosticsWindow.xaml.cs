using System;
using System.Linq;
using System.Threading;
using System.Windows;
using HonestFlow.Application.PointStatus;

namespace HonestFlow.UI;

public partial class DeveloperDiagnosticsWindow : Window
{
    private readonly DeveloperDiagnosticsSession _session;
    private readonly CancellationTokenSource _lifetime = new();

    public DeveloperDiagnosticsWindow(DeveloperDiagnosticsSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        InitializeComponent();
        Render(_session.Snapshot);
        Closed += (_, _) => _lifetime.Cancel();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Выполняется проверка…";
        try
        {
            DiagnosticsSnapshot snapshot = await _session.RefreshAsync(_lifetime.Token);
            Render(snapshot);
            StatusText.Text = "Проверка завершена.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            StatusText.Text = $"Ошибка проверки: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested) RefreshButton.IsEnabled = true;
        }
    }

    private void Render(DiagnosticsSnapshot snapshot)
    {
        WorkStateText.Text = snapshot?.WorkState.ToString() ?? "No snapshot";
        SnapshotTimestampText.Text = snapshot?.ObservedAtUtc.ToString("O") ?? "-";
        IssuesGrid.ItemsSource = snapshot?.Issues.Select(issue => new
        {
            issue.Code,
            issue.Component,
            issue.Severity,
            issue.Title,
            issue.UserMessage,
            SuggestedFix = issue.SuggestedFix?.ToString() ?? "-",
            issue.TechnicalDetails,
            Evidence = string.Join("; ", issue.Evidence.Select(item => $"{item.Key}={item.Value}"))
        }).ToArray();
        FactsGrid.ItemsSource = snapshot?.Facts;
    }
}

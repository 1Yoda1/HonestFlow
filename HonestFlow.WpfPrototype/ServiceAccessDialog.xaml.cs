using System;
using System.Threading;
using System.Windows;
using HonestFlow.Application.Installation;

namespace HonestFlow.WpfPrototype;

public partial class ServiceAccessDialog : Window
{
    private readonly ServiceInstallationAccessWorkflow _workflow;
    private readonly string _architecture;
    private readonly string _appVersion;
    private readonly CancellationToken _cancellationToken;

    public ServiceAccessDialog(
        ServiceInstallationAccessWorkflow workflow,
        string architecture,
        string appVersion,
        CancellationToken cancellationToken)
    {
        InitializeComponent();
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _architecture = architecture;
        _appVersion = appVersion;
        _cancellationToken = cancellationToken;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    public ServiceInstallationSession? Session { get; private set; }

    private async void Continue_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        ContinueButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        try
        {
            ServiceInstallationAccessResult result = await _workflow.AuthorizeAsync(
                PasswordInput.Password, _architecture, _appVersion, _cancellationToken);
            PasswordInput.Clear();
            if (!result.IsGranted)
            {
                ErrorText.Text = result.Message;
                ErrorText.Visibility = Visibility.Visible;
                PasswordInput.Focus();
                return;
            }

            Session = result.Session;
            DialogResult = true;
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            Close();
        }
        finally
        {
            ContinueButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        PasswordInput.Clear();
        DialogResult = false;
    }
}

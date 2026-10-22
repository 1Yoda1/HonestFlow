using System.Windows;

namespace HonestFlow.UI;

public partial class SupportRequestDialog : Window
{
    public SupportRequestDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => ProblemTextBox.Focus();
    }

    public string ProblemDescription => ProblemTextBox.Text.Trim();

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        if (ProblemDescription.Length < 3)
        {
            ValidationText.Text = "Опишите проблему хотя бы в нескольких словах.";
            ProblemTextBox.Focus();
            return;
        }

        DialogResult = true;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using HonestFlow.Application.PointStatus;

namespace HonestFlow.UI;

public partial class InstructionWindow : Window
{
    public InstructionWindow(IReadOnlyList<DiagnosticIssue> issues)
    {
        InitializeComponent();
        IssueTitlesList.ItemsSource = (issues ?? Array.Empty<DiagnosticIssue>())
            .Select(issue => issue.Title)
            .ToArray();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

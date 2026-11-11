using System.Windows;
using PromptLibrary.Core.Exchange;

namespace PromptLibrary.App.Views;

/// <summary>
/// Shown after an import: how many prompts were imported, how many were skipped as duplicates and how many files failed,
/// and, when some failed, a scrollable list of <c>file — reason</c>.
/// </summary>
public partial class ImportResultsDialog : Window
{
    /// <summary>One row of the failure list.</summary>
    public sealed record FailureRow(string File, string Reason)
    {
        public string Line => $"{File} — {Reason}";
    }

    public ImportResultsDialog(ImportResult result)
    {
        InitializeComponent();
        ImportedCount.Text = result.Imported.ToString();
        DuplicateCount.Text = result.Duplicates.ToString();
        FailedCount.Text = result.Unusable.ToString();
        if (result.Unusable > 0)
            FailedCount.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger");

        var failures = result.Failures;
        FailureList.ItemsSource = failures.Select(f => new FailureRow(f.File, f.Reason)).ToList();
        FailurePanel.Visibility = failures.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    internal bool ShowsFailureList => FailurePanel.Visibility == Visibility.Visible;
}

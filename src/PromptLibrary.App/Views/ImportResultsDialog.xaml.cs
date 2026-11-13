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
        /// <summary>What to do about it, in plain words, or empty.</summary>
        public string Fix => FixFor(Reason);

        public string Line => Fix.Length == 0 ? $"{File} — {Reason}" : $"{File} — {Reason}. {Fix}";
    }

    /// <summary>The next step for each reason an import can fail.</summary>
    internal static string FixFor(string reason) => reason switch
    {
        "empty" => "Add the prompt text to the file, then import it again.",
        "couldn't be read" => "Close any app that has it open, or copy it to your PC, then try again.",
        "damaged or not a prompt file" or "not a Prompt Library export" => "Pick a file you exported from Prompt Library.",
        "made by a newer Prompt Library" => "Update Prompt Library (Help → Check for updates), then import it again.",
        _ when reason.StartsWith("larger than", StringComparison.Ordinal) => "Split it into smaller files, then import them.",
        _ => "",
    };

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

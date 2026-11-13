using System.Windows;
using PromptLibrary.App.ViewModels;

namespace PromptLibrary.App.Views;

/// <summary>
/// The placeholder form as a small dialog of the main window (560 px wide, centred on its owner). Fill, then Copy (or
/// Send): <see cref="Ask"/> returns the filled text, or null when the user cancelled.
/// </summary>
public partial class FillInDialog : Window
{
    /// <summary>The dialog grows with its form up to this share of the work area's height; the fields scroll beyond it.</summary>
    public const double MaxWorkAreaShare = 0.8;

    private string? _result;
    private int? _caretBackSteps;

    private FillInDialog(FillInViewModel model)
    {
        InitializeComponent();
        MaxHeight = Math.Max(MinHeight, WindowFit.WorkAreaHeight * MaxWorkAreaShare);
        Panel.DataContext = model;
        model.Completed += (text, caretBackSteps) =>
        {
            _result = text;
            _caretBackSteps = caretBackSteps;
            DialogResult = true;
        };
        model.Cancelled += () => DialogResult = false;
    }

    /// <summary>Shows the form; returns the filled text, or null on cancel (Esc and the close button cancel too).</summary>
    public static string? Ask(Window? owner, FillInViewModel model) => AskForResult(owner, model)?.Text;

    /// <summary>As <see cref="Ask"/>, with the {cursor} caret steps too (the shortcut menu pastes and moves the caret).</summary>
    public static Core.Templates.FillResult? AskForResult(Window? owner, FillInViewModel model)
    {
        var dialog = new FillInDialog(model);
        if (owner is { IsVisible: true })
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true ? new Core.Templates.FillResult(dialog._result!, dialog._caretBackSteps) : null;
    }
}

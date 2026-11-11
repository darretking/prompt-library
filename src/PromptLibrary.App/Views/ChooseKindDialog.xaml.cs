using System.Windows;
using System.Windows.Automation;

namespace PromptLibrary.App.Views;

/// <summary>
/// A small question with two radio answers: <c>One file (.json)</c> or <c>A folder of Markdown files</c>. Used by
/// Import prompts… and Export prompts…; <see cref="IsFolder"/> is the second answer.
/// </summary>
public partial class ChooseKindDialog : Window
{
    public ChooseKindDialog(string title, string message, string fileOption, string folderOption, string primary)
    {
        InitializeComponent();
        Title = title;
        AutomationProperties.SetName(this, title);
        MessageText.Text = message;
        FileOption.Content = fileOption;
        FolderOption.Content = folderOption;
        AutomationProperties.SetName(FileOption, fileOption);
        AutomationProperties.SetName(FolderOption, folderOption);
        PrimaryButton.Content = primary;
        AutomationProperties.SetName(PrimaryButton, primary.TrimEnd('…'));
        Loaded += (_, _) => FileOption.Focus();
    }

    /// <summary>True when the second answer (a folder) was chosen.</summary>
    public bool IsFolder => FolderOption.IsChecked == true;

    private void Primary_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}

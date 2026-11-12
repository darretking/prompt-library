using System.Windows.Controls;
using System.Windows.Media;
using PromptLibrary.App.Theming;
using PromptLibrary.App.Views;
using PromptLibrary.Core.Exchange;
using PromptLibrary.Core.Models;

namespace PromptLibrary.App.Tests;

/// <summary>Dialog text that must follow the user's choice and stay readable in both themes.</summary>
[Collection("WpfApplication")]
public class DialogTextTests
{
    [Fact]
    public void DeleteCategory_TickingDeletePrompts_RewritesTheQuestion() => Sta.RunOnApp(() =>
    {
        ChoiceTypingTests.StartApp();
        var other = new Category { Name = "Work" };
        var dialog = new MoveCategoryDialog("Travel", 3, [other]);
        Assert.Contains("Move them to another category", dialog.Message);

        dialog.DeleteTickBox.IsChecked = true;
        Assert.Equal("\"Travel\" and its 3 prompts will be deleted.", dialog.Message);
        Assert.DoesNotContain("Move", dialog.Message);

        dialog.DeleteTickBox.IsChecked = false;
        Assert.Contains("Move them to another category", dialog.Message);
        dialog.Close();
    });

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void ImportResults_FailedFileNames_UseTheInkColour(string theme) => Sta.RunOnApp(() =>
    {
        ChoiceTypingTests.StartApp();
        ThemeManager.Current.Apply(theme);
        try
        {
            var dialog = new ImportResultsDialog(new ImportResult([new("broken.json", 0, 0, 0, "not valid JSON")]))
            {
                Left = -10000, Top = -10000, ShowActivated = false,
            };
            dialog.Show();
            Sta.DoEvents();
            var row = ChoiceTypingTests.Descendants<TextBlock>(dialog).Single(t => System.Windows.Automation.AutomationProperties.GetName(t).StartsWith("broken.json"));
            var ink = ((SolidColorBrush)System.Windows.Application.Current.Resources["Brush.Ink"]).Color;
            Assert.Equal(ink, ((SolidColorBrush)row.Foreground).Color);
            dialog.Close();
        }
        finally { ThemeManager.Current.Apply("light"); }
    });
}

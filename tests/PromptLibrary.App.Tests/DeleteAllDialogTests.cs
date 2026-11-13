using PromptLibrary.App.Views;

namespace PromptLibrary.App.Tests;

/// <summary>Settings → Delete all my prompts: the typed DELETE gate and what the dialog says.</summary>
[Collection("WpfApplication")]
public class DeleteAllDialogTests
{
    [Fact]
    public void DeleteButton_StaysOffUntilDeleteIsTyped() => Sta.RunOnApp(() =>
    {
        ChoiceTypingTests.StartApp();
        var dialog = new DeleteAllDialog(39, 13);
        Assert.False(dialog.DeleteActionButton.IsEnabled);

        foreach (var typed in new[] { "D", "DELET", "DELETE.", "DELETE ALL", "yes" })
        {
            dialog.Box.Text = typed;
            Assert.False(dialog.DeleteActionButton.IsEnabled, typed);
        }

        dialog.Box.Text = " delete ";
        Assert.True(dialog.DeleteActionButton.IsEnabled);

        dialog.Box.Text = "DELETE";
        Assert.True(dialog.DeleteActionButton.IsEnabled);

        dialog.Box.Text = "";
        Assert.False(dialog.DeleteActionButton.IsEnabled);
        dialog.Close();
    });

    [Theory]
    [InlineData(39, 13, "This deletes 39 prompts and 13 categories from Your prompts.")]
    [InlineData(1, 1, "This deletes 1 prompt and 1 category from Your prompts.")]
    [InlineData(0, 2, "This deletes 2 categories from Your prompts.")]
    [InlineData(5, 0, "This deletes 5 prompts from Your prompts.")]
    public void Message_CountsWhatGoes(int prompts, int categories, string expected)
        => Assert.Equal(expected, DeleteAllDialog.Message(prompts, categories));
}

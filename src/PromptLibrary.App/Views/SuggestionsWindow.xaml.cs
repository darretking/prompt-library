using System.Windows;
using PromptLibrary.App.ViewModels;

namespace PromptLibrary.App.Views;

/// <summary>The editors' Suggestions window: add a suggestion to the team prompts or dismiss it.</summary>
public partial class SuggestionsWindow : Window
{
    public SuggestionsWindow(SuggestionsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using PromptLibrary.App.ViewModels;

namespace PromptLibrary.App.Views;

/// <summary>
/// Where backups go: detected cloud folders as cards, "Choose another folder…", results and the last backup time.
/// Shared by the Settings window and the onboarding wizard; call <see cref="Attach"/> once. Cloud folders are
/// looked for again whenever the hosting window is activated (e.g. after signing in to OneDrive elsewhere).
/// </summary>
public partial class BackupLocationPicker : UserControl
{
    private Window? _hostWindow;

    public BackupLocationPicker()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            if (_hostWindow is not null) _hostWindow.Activated -= HostWindow_Activated;
            _hostWindow = null;
        };
    }

    public BackupLocationPickerViewModel? ViewModel { get; private set; }

    /// <summary>
    /// Clicking a card only selects it; the host backs up later (the wizard's Next). Default false: a click backs up
    /// there at once. Set before <see cref="Attach"/>.
    /// </summary>
    public bool SelectsWithoutBackingUp { get; set; }

    /// <summary>Creates the view model for <paramref name="host"/> and starts looking for cloud folders.</summary>
    public BackupLocationPickerViewModel Attach(AppHost host)
    {
        ViewModel = new BackupLocationPickerViewModel(host, ChooseFolder, SelectsWithoutBackingUp);
        DataContext = ViewModel;
        _ = ViewModel.RefreshAsync(showProgress: true);
        return ViewModel;
    }

    /// <summary>
    /// Puts the keyboard focus on the first card. If the cards aren't there yet (detection still running), focus
    /// goes to "Choose another folder…" and moves to the first card when it appears.
    /// </summary>
    public void FocusFirstCard()
    {
        if (ViewModel is { Cards.Count: 0 } vm)
        {
            void OnCardsChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
            {
                if (vm.Cards.Count == 0) return;
                vm.Cards.CollectionChanged -= OnCardsChanged;
                FocusFirstCard();
            }
            vm.Cards.CollectionChanged += OnCardsChanged;
        }

        // After layout and activation, so the card exists and window activation doesn't take the focus back.
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            if (CardList.ItemContainerGenerator.ContainerFromIndex(0) is DependencyObject first
                && FindButton(first) is { } card)
                Keyboard.Focus(card);
            else if (ChooseFolderButton.IsVisible)
                Keyboard.Focus(ChooseFolderButton);
        });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hostWindow = Window.GetWindow(this);
        if (_hostWindow is not null) _hostWindow.Activated += HostWindow_Activated;
    }

    private void HostWindow_Activated(object? sender, EventArgs e)
        => _ = ViewModel?.RefreshAsync(showProgress: false);

    private static Button? FindButton(DependencyObject element)
    {
        if (element is Button button) return button;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); i++)
            if (FindButton(System.Windows.Media.VisualTreeHelper.GetChild(element, i)) is { } found)
                return found;
        return null;
    }

    private string? ChooseFolder(string? startIn)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder for backups" };
        if (startIn is not null && Directory.Exists(startIn))
            dialog.InitialDirectory = startIn;
        return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FolderName : null;
    }
}

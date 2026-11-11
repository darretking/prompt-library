using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PromptLibrary.App.Theming;
using PromptLibrary.App.ViewModels;
using PromptLibrary.App.Views;
using PromptLibrary.App.Views.Controls;
using PromptLibrary.Core.History;
using PromptLibrary.Core.Hotkeys;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Seed;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.App.Tests.Gallery;

/// <summary>
/// Renders every screen and state of the app offscreen to PNGs (light and dark, 100 % and 150 %) so the UI can be reviewed
/// without launching the app. Runs only when PL_SCREENSHOTS holds an absolute output folder (see tools/make-gallery.ps1).
/// Windows are shown far off screen and drawn with RenderTargetBitmap (live screen capture isn't possible here); popups and
/// menus are measured and drawn on their own. Mica is not shown: the window backdrop tokens are forced opaque.
/// Sandbox data only: the app's data folder is a temp folder, the real library and settings are never read.
/// </summary>
public sealed class ScreenshotGallery
{
    private static readonly DateTimeOffset Fixed = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly (string Name, double Scale)[] Scales = [("100", 1), ("150", 1.5)];

    private string _out = "";
    private string _theme = "";
    private readonly List<string> _failures = [];
    private readonly List<string> _written = [];

    [GalleryFact]
    public void RenderAllScreens() => Sta.Run(() =>
    {
        _out = GalleryFactAttribute.OutputFolder!;
        // Relative pack URIs (window icons) resolve against Application.ResourceAssembly, which is already testhost here: point it at the app.
        typeof(Application).GetField("_resourceAssembly", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, typeof(App).Assembly);
        if (Application.Current is null)
            new App().InitializeComponent(); // the styles and brushes; OnStartup never runs (no tray, no hotkey)

        foreach (var theme in new[] { "light", "dark" })
        {
            _theme = theme;
            ThemeManager.Current.Apply(theme);
            MakeBackdropsOpaque();
            foreach (var (name, scene) in Scenes())
            {
                try { scene(name); }
                catch (Exception ex) { _failures.Add($"{theme} {name}: " + string.Join(" <- ", Chain(ex))); }
            }
        }

        Directory.CreateDirectory(_out);
        File.WriteAllLines(Path.Combine(_out, "_log.txt"), _written.Concat(_failures.Select(f => "FAILED " + f)));
        Assert.True(_failures.Count == 0, string.Join(Environment.NewLine, _failures));
    }, timeoutSeconds: 600);

    private static IEnumerable<string> Chain(Exception? ex) { for (; ex is not null; ex = ex.InnerException) yield return ex.GetType().Name + ": " + ex.Message; }

    // ---- The screens ---------------------------------------------------------------------------

    private IEnumerable<(string, Action<string>)> Scenes()
    {
        yield return ("01-main", n => Main(n, w => { }));
        yield return ("02-main-preview", n => Main(n, w => w.Vm.Editor!.IsPreview = true));
        yield return ("03-main-search-itin-trp", n => Main(n, w =>
        {
            w.Vm.SelectedCategory = w.Vm.Categories.First(c => c.IsAll);
            w.Vm.SearchText = "itin trp";
        }));
        yield return ("04-main-search-no-match", n => Main(n, w =>
        {
            w.Vm.SelectedCategory = w.Vm.Categories.First(c => c.IsAll);
            w.Vm.SearchText = "zzqxv";
        }));
        yield return ("05-main-empty-library", n => Main(n, w => { }, populated: false));
        yield return ("06-main-unsaved-and-error", n => Main(n, w =>
        {
            w.Vm.Editor!.Title = "";
            w.Vm.SaveCommand.Execute(null);
        }));
        yield return ("07-overflow-menu", n => Main(n, w => { }, after: win => Menu(n, win.OverflowButton, win)));
        yield return ("08-send-menu", n => Main(n, w => { }, after: win => SendMenu(n, win)));
        yield return ("09-emoji-picker", n => Main(n, w => { }, after: win =>
        {
            var button = Find<EmojiButton>(win) ?? throw new InvalidOperationException("no EmojiButton in the main window");
            var popup = (System.Windows.Controls.Primitives.Popup)button.FindName("Picker");
            Capture(n, Isolate(popup.Child));
        }));
        yield return ("10-history", History);
        yield return ("11-fill-in-dialog", FillIn);
        yield return ("12-delete-category-dialog", DeleteCategory);
        yield return ("13-export-dialog", n => Window(n, new ChooseKindDialog("Export prompts",
            "Export your prompts so you can keep a copy or use them elsewhere.", "One file (.json)", "A folder of Markdown files", "Choose…"), null, null));
        yield return ("14-search-empty", n => Search(n, vm => { }));
        yield return ("15-search-budg", n => Search(n, vm => vm.Query = "budg"));
        yield return ("16-search-actions", n => Search(n, vm => { vm.Query = "budg"; vm.Move(0); vm.OpenActions(); }));
        yield return ("17-search-fill-in", n => Search(n, vm => { vm.Query = "plan a trip"; vm.Move(0); vm.UseSelected(); }));
        yield return ("18-settings", n => Window(n, new SettingsWindow(NewHost(populated: true)), null, null));
        yield return ("19-onboarding-step1", n => Onboarding(n, last: false));
        yield return ("20-onboarding-last-step", n => Onboarding(n, last: true));
        yield return ("21-main-minimum-size", n => Main(n, w => { }, width: 860, height: 520));
        // 22 (team side) is skipped: it needs a joined TeamLibraryService (file-backed team source and cache), which can't be
        // built from the sandbox without product changes.
    }

    private sealed class World(MainWindow window, Prompt? trip)
    {
        public MainViewModel Vm => window.ViewModel;
        public Prompt? Trip { get; } = trip;
    }

    private void Main(string name, Action<World> setup, bool populated = true, double width = 1100, double height = 700,
        Action<MainWindow>? after = null)
    {
        var host = NewHost(populated);
        var window = new MainWindow(host);
        var trip = host.Library.Prompts.FirstOrDefault(p => p.Title == "Plan a trip itinerary");
        var world = new World(window, trip);
        window.Width = width;
        window.Height = height;
        Place(window);
        window.Show();
        Pump();
        try
        {
            if (trip is not null)
            {
                world.Vm.SelectedCategory = world.Vm.Categories.First(c => c.Id == trip.CategoryId);
                world.Vm.SelectedPrompt = world.Vm.Prompts.First(r => r.Id == trip.Id);
            }
            setup(world);
            Pump();
            Capture(name, window);
            after?.Invoke(window);
        }
        finally { window.CloseForExit(); }
    }

    private void History(string name)
    {
        var host = NewHost(true);
        var now = DateTimeOffset.Now;
        PromptVersion V(TimeSpan ago, string title, string text) => new() { SavedAt = now - ago, Title = title, Text = text };
        var versions = new List<PromptVersion>
        {
            V(TimeSpan.FromHours(3), "Plan a trip itinerary", "Plan a {number}-day trip to {destination}.\nBudget: {budget}.\nGive me a table."),
            V(TimeSpan.FromDays(2), "Plan a trip itinerary", "Plan a {number}-day trip to {destination} for {who}.\nBudget: {budget}."),
            V(TimeSpan.FromDays(20), "Trip itinerary", "Plan a trip to {destination}."),
        };
        var model = new HistoryViewModel(versions,
            "Plan a {number}-day trip to {destination} for {who}.\nBudget: {budget}.\nGive me a day-by-day table.", now, _ => { });
        model.Selected = model.Items[1];
        Window(name, new HistoryWindow(host, model), null, null);
    }

    private void FillIn(string name)
    {
        var host = NewHost(true);
        var trip = host.Library.Prompts.First(p => p.Title == "Plan a trip itinerary");
        var model = new FillInViewModel(trip.Text, host.Settings, new Tokens(), "Copy", trip.Title, cancelLabel: "Cancel");
        var dialog = (Window)Activator.CreateInstance(typeof(FillInDialog),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [model], null)!;
        Window(name, dialog, null, null);
    }

    private void DeleteCategory(string name)
    {
        var host = NewHost(true);
        var travel = host.Library.Categories.First(c => c.Name == "Travel");
        var dialog = new MoveCategoryDialog("Travel", host.Library.Prompts.Count(p => p.CategoryId == travel.Id),
            host.Library.Categories.Where(c => c != travel).ToList());
        dialog.DeleteBox.IsChecked = true;
        Window(name, dialog, null, null);
    }

    private void Onboarding(string name, bool last)
    {
        var host = NewHost(true);
        host.Settings.FirstRunDone = false;
        var window = new OnboardingWindow(host);
        if (last)
        {
            window.ViewModel.NextCommand.Execute(null);
            Pump();
            window.ViewModel.NextCommand.Execute(null);
            Pump();
            window.ViewModel.NextCommand.Execute(null);
            Pump();
            window.ViewModel.SkipBackupsCommand.Execute(null);
            Pump();
            if (!window.ViewModel.IsReady)
                throw new InvalidOperationException("onboarding did not reach the last step; at " + window.ViewModel.Step);
        }
        Window(name, window, null, null);
    }

    private void Search(string name, Action<SearchViewModel> setup)
    {
        var host = NewHost(true);
        var vm = new SearchViewModel(new SearchActions(host.Library, host.Settings), () => DateTimeOffset.Now);
        var window = new SearchWindow(vm);
        vm.Reset();
        setup(vm);
        Window(name, window, 640, 420);
    }

    // ---- Rendering -----------------------------------------------------------------------------

    private void Window(string name, Window window, double? width, double? height)
    {
        if (width is { } w) window.Width = w;
        if (height is { } h) window.Height = h;
        Place(window);
        window.Show();
        Pump();
        try { Capture(name, window); }
        finally
        {
            try { if (window is OnboardingWindow onboarding) onboarding.CloseForExit(); else window.Close(); } catch (Exception) { }
        }
    }

    private static void Place(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = 100;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
    }

    /// <summary>The Send menu is built in code when the arrow is pressed (MainWindow.SendMenu_Click); this builds the same menu without opening it.</summary>
    private void SendMenu(string name, MainWindow window)
    {
        var item = (Style)window.FindResource("FlyoutItem");
        var menu = new ContextMenu { Style = (Style)window.FindResource("FlyoutMenu"), DataContext = window.DataContext };
        foreach (var choice in window.ViewModel.SendChoices)
            menu.Items.Add(new MenuItem { Style = item, Header = choice.Name, IsChecked = choice.IsDefault });
        menu.Items.Add(new Separator { Style = (Style)window.FindResource("FlyoutSeparator") });
        menu.Items.Add(new MenuItem { Style = item, Header = "Set as default for this prompt" });
        Capture(name, Isolate(menu));
    }

    /// <summary>Draws a button's context menu (a popup, so it isn't part of the window) as its own picture.</summary>
    private void Menu(string name, Button button, MainWindow window)
    {
        var menu = button.ContextMenu!;
        menu.DataContext = window.DataContext;
        menu.PlacementTarget = button;
        Capture(name, Isolate(menu));
    }

    /// <summary>Lays an element that normally lives in a popup out on its own (without opening a real popup).</summary>
    private static FrameworkElement Isolate(UIElement element)
    {
        var fe = (FrameworkElement)element;
        fe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        fe.Arrange(new Rect(fe.DesiredSize));
        fe.UpdateLayout();
        if (fe.ActualWidth < 1) throw new InvalidOperationException("element has no size: " + fe.GetType().Name);
        return fe;
    }

    private void Capture(string name, FrameworkElement element)
    {
        var isWindow = element is Window;
        double w = element.ActualWidth, h = element.ActualHeight;
        var margin = isWindow ? 0 : 12; // a little of the window colour around a loose popup
        foreach (var (folder, scale) in Scales)
        {
            var dir = Path.Combine(_out, $"{_theme}-{folder}");
            Directory.CreateDirectory(dir);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle((Brush)Application.Current.Resources["Brush.Window"], null, new Rect(0, 0, w + 2 * margin, h + 2 * margin));
                dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                    null, new Rect(margin, margin, w, h));
            }
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling((w + 2 * margin) * scale), (int)Math.Ceiling((h + 2 * margin) * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var file = Path.Combine(dir, name + ".png");
            using (var stream = File.Create(file)) encoder.Save(stream);
            _written.Add(file);
        }
    }

    /// <summary>Runs everything queued on the dispatcher, down to idle, a few times (AvalonEdit renders late).</summary>
    private static void Pump()
    {
        for (var i = 0; i < 4; i++)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(40);
        }
    }

    /// <summary>With Mica the window and rail brushes are translucent tints; the gallery paints them solid instead.</summary>
    private static void MakeBackdropsOpaque()
    {
        var resources = Application.Current.Resources;
        resources["Brush.WindowBackdrop"] = resources["Brush.Window"];
        resources["Brush.RailBackdrop"] = resources["Brush.Rail"];
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;
        var children = LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().Concat(VisualChildren(root)).ToList();
        foreach (var child in children)
            if (Find<T>(child) is { } found) return found;
        return null;
    }

    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject root)
    {
        if (root is not Visual and not System.Windows.Media.Media3D.Visual3D) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            yield return VisualTreeHelper.GetChild(root, i);
    }

    // ---- Sandbox data --------------------------------------------------------------------------

    /// <summary>An un-started AppHost (no tray, hotkey or backups) holding the seed library (or an empty one) and some usage.</summary>
    private static AppHost NewHost(bool populated)
    {
        var host = new AppHost();
        var library = populated ? SeedLibrary.Create(Fixed) : new Library();
        typeof(AppHost).GetProperty(nameof(AppHost.Library))!.SetValue(host, library);
        typeof(AppHost).GetField("_activeGesture", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(host, HotkeyGesture.TryParse(host.Settings.Hotkey, out var g) ? g : null);
        host.Settings.Theme = ThemeManager.Current.Mode;
        host.Settings.FirstRunDone = true;

        if (populated)
        {
            Prompt P(string title) => library.Prompts.First(p => p.Title == title);
            var now = DateTimeOffset.Now;
            host.Settings.PinnedPromptIds.AddRange([P("Plan a trip itinerary").Id, P("Make a monthly budget").Id]);
            string[] used = ["Plan a trip itinerary", "Make a monthly budget", "Social media content calendar", "Ad copy in five angles"];
            for (var i = 0; i < used.Length; i++)
                host.Settings.Usage[P(used[i]).Id] = new PromptUsage { Count = 5 - i, LastUsedAt = now.AddHours(-3 - 20 * i) };
        }
        return host;
    }

    private sealed class Tokens : ITokenValues
    {
        public string? Clipboard => null;
        public DateTime Now => new(2026, 11, 11, 12, 0, 0);
        public CultureInfo Culture => CultureInfo.GetCultureInfo("en-GB");
    }

    private sealed class SearchActions(Library library, AppSettings settings) : ISearchActions
    {
        public Library Library { get; } = library;
        public Library? TeamLibrary => null;
        public AppSettings Settings { get; } = settings;
        public ITokenValues Tokens { get; } = new Tokens();
        public void Paste(Prompt prompt, string text, int? caretBackSteps) { }
        public void CopyOnly(Prompt prompt, string text) { }
        public void Send(Prompt prompt, string text, string targetId) { }
        public void OpenInMainWindow(Prompt prompt, bool isTeam) { }
        public void PinsChanged() { }
        public void Close() { }
    }
}

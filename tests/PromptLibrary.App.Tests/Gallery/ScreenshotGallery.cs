using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PromptLibrary.App.Search;
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
[Collection("WpfApplication")]
public sealed class ScreenshotGallery
{
    private static readonly DateTimeOffset Fixed = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly (string Name, double Scale)[] Scales = [("100", 1), ("150", 1.5), ("200", 2)];

    /// <summary>The screens also drawn at 200 % (light-200, dark-200); the others only at 100 % and 150 %.</summary>
    private static readonly string[] AtTwoHundred = ["01-main", "10-history", "11-fill-in-dialog", "12-delete-category-dialog", "14-search-empty", "17-search-fill-in", "18-settings"];

    private string _out = "";
    private string _theme = "";
    private readonly List<string> _failures = [];
    private readonly List<string> _written = [];

    [GalleryFact]
    public void RenderAllScreens() => Sta.Run(() =>
    {
        _out = GalleryFactAttribute.OutputFolder!;
        Motion.Suppress = true; // never a picture half way through a fade (read when App.xaml loads)
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
        yield return ("02-main-preview", n => Main(n, w =>
        {
            // Some fields remembered from the last fill-in, the rest still empty (muted chips).
            w.Vm.PlaceholderValues["number"] = "4";
            w.Vm.PlaceholderValues["destination"] = "Lisbon";
            w.Vm.PlaceholderValues["who's going"] = "two adults";
            w.Vm.PlaceholderValues["relaxed / packed"] = "relaxed";
            w.Vm.Editor!.IsPreview = true;
        }));
        yield return ("23-import-results", ImportResults);
        yield return ("25-file-menu", n => Main(n, w => { }, after: win => FileMenu(n, win)));
        // The frame itself (native caption buttons, Mica) is drawn by DWM and cannot be captured here: this is the title bar row
        // as WPF draws it, active and inactive (the 144 px on the right are where the native buttons sit).
        yield return ("29-title-bar", n => Main(n, w => { }, capture: false, after: win => Capture(n, win.TitleBar)));
        yield return ("29b-title-bar-inactive", n => Main(n, w => { w.Vm.Editor!.Title += " edit"; }, capture: false, inactiveTitle: true,
            after: win => Capture(n, win.TitleBar)));
        yield return ("26-main-preview-markdown", n => Main(n, w =>
        {
            w.Vm.PlaceholderValues["owner"] = "Priya";
            w.Vm.Editor!.Text = "# Weekly review\n\nWrite a **short** summary of {topic} for {date}, {time}.\n\n" +
                                "- Wins this week\n- Blockers\n  - owner: {owner}\n2. Next steps\n\n```bash\nnpm test {cursor}\n```\n\nContext: {clipboard}";
            w.Vm.Editor.IsPreview = true;
        }));
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
        yield return ("12-delete-category-dialog", n => DeleteCategory(n, deletePrompts: true));
        yield return ("24-delete-category-move", n => DeleteCategory(n, deletePrompts: false));
        yield return ("13-export-dialog", n => Window(n, new ChooseKindDialog("Export prompts",
            "Export your prompts so you can keep a copy or use them elsewhere.", "One file (.json)", "A folder of Markdown files", "Choose…"), null, null));
        yield return ("14-search-empty", n => Search(n, vm => { }));
        yield return ("15-search-budg", n => Search(n, vm => vm.Query = "budg"));
        yield return ("16-search-actions", n => Search(n, vm => { vm.Query = "budg"; vm.Move(0); vm.OpenActions(); }));
        yield return ("17-search-fill-in", n => Search(n, vm => { vm.Query = "plan a trip"; vm.Move(0); vm.UseSelected(); }, fitForm: true));
        yield return ("27-search-no-results", n => Search(n, vm => vm.Query = "zzzz"));
        yield return ("28-search-over-backdrop", SearchOverBackdrop);
        yield return ("30-search-opening", SearchOpening);
        yield return ("17b-search-tags", n => Search(n, vm => { vm.Query = "kids"; vm.Move(0); }));
        yield return ("18-settings", n => Window(n, new SettingsWindow(NewHost(populated: true)), null, null));
        yield return ("19-onboarding-step1", n => Onboarding(n, last: false));
        yield return ("20-onboarding-last-step", n => Onboarding(n, last: true));
        yield return ("21-main-minimum-size", n => Main(n, w => { }, width: 860, height: 520));
        yield return ("22-toast", n => Toast(n, "Pasted: Plan a trip itinerary", Core.Clipboard.ToastKind.Success));
        yield return ("23-toast-error", n => Toast(n, Core.Clipboard.PasteDecision.CouldntPasteText, Core.Clipboard.ToastKind.Error));
        // The team side is skipped: it needs a joined TeamLibraryService (file-backed team source and cache), which can't be
        // built from the sandbox without product changes.
    }

    private sealed class World(MainWindow window, Prompt? trip)
    {
        public MainViewModel Vm => window.ViewModel;
        public Prompt? Trip { get; } = trip;
    }

    private void Main(string name, Action<World> setup, bool populated = true, double width = 1100, double height = 700,
        Action<MainWindow>? after = null, bool capture = true, bool inactiveTitle = false)
    {
        var host = NewHost(populated);
        var window = new MainWindow(host);
        var trip = host.Library.Prompts.FirstOrDefault(p => p.Title == "Plan a trip itinerary");
        var world = new World(window, trip);
        window.Width = width;
        window.Height = height;
        Place(window);
        window.Show();
        window.Activate();
        // The offscreen window is never really active, so its title would show dimmed. A window in use shows it in Ink: say so
        // here (a local value beats the style's inactive trigger), except for the screen that shows the inactive look.
        if (!inactiveTitle && window.TitleBar.Children.OfType<TextBlock>().FirstOrDefault() is { } title)
            title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Ink");
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
            if (capture) Capture(name, window);
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

    /// <summary>Values a returning user would have typed before, so the fill-in shots show remembered answers.</summary>
    private static void RememberTripValues(AppSettings settings)
    {
        settings.PlaceholderValues["number"] = "5";
        settings.PlaceholderValues["destination"] = "Lisbon";
        settings.PlaceholderValues["who's going"] = "two adults";
        settings.PlaceholderValues["budget"] = "mid-range";
        settings.PlaceholderValues["relaxed / packed"] = "relaxed";
    }

    private void FillIn(string name)
    {
        var host = NewHost(true);
        var trip = host.Library.Prompts.First(p => p.Title == "Plan a trip itinerary");
        RememberTripValues(host.Settings);
        var model = new FillInViewModel(trip.Text, host.Settings, new Tokens(), "Copy", trip.Title, cancelLabel: "Cancel");
        var dialog = (Window)Activator.CreateInstance(typeof(FillInDialog),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [model], null)!;
        Window(name, dialog, null, null);
    }

    private void DeleteCategory(string name, bool deletePrompts)
    {
        var host = NewHost(true);
        var travel = host.Library.Categories.First(c => c.Name == "Travel");
        var dialog = new MoveCategoryDialog("Travel", host.Library.Prompts.Count(p => p.CategoryId == travel.Id),
            host.Library.Categories.Where(c => c != travel).ToList());
        // Never contradictory: ticked = no "Move prompts to" at all and a Danger warning; unticked = a category already chosen.
        Assert.True(dialog.IsMoveChoiceShown);
        Assert.NotNull(dialog.ChosenTarget);
        Assert.Equal("Prompts move to the category you pick.", dialog.HintMessage);
        if (deletePrompts)
        {
            dialog.DeleteTickBox.IsChecked = true;
            Assert.False(dialog.IsMoveChoiceShown);
            Assert.Equal("This can't be undone.", dialog.HintMessage);
        }
        Window(name, dialog, null, null);
    }

    private void ImportResults(string name)
    {
        var result = new PromptLibrary.Core.Exchange.ImportResult(
        [
            new("Coding/Explain this code.md", 1, 0, 0, null),
            new("Coding/Refactor.md", 1, 0, 0, null),
            new("Email/Follow-up.md", 0, 1, 0, null),
            new("Email/notes.txt", 0, 0, 0, "empty"),
            new("Travel/everything.md", 0, 0, 0, "larger than 1 MB"),
            new("old-export.json", 0, 0, 0, "not a Prompt Library export"),
            new("broken.json", 0, 0, 0, "not valid JSON"),
        ]);
        Window(name, new ImportResultsDialog(result), null, null);
    }

    /// <summary>The File menu's drop-down, drawn on its own (popups are not part of the window picture).</summary>
    private void FileMenu(string name, MainWindow window)
    {
        var file = Find<MenuItem>(window) ?? throw new InvalidOperationException("no menu in the main window");
        var popup = (System.Windows.Controls.Primitives.Popup)file.Template.FindName("PART_Popup", file);
        Capture(name, Isolate(popup.Child));
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

    private void Search(string name, Action<SearchViewModel> setup, bool fitForm = false)
    {
        var host = NewHost(true);
        RememberTripValues(host.Settings);
        var vm = new SearchViewModel(new SearchActions(host.Library, host.Settings), () => DateTimeOffset.Now);
        var window = new SearchWindow(vm);
        vm.Reset();
        setup(vm);
        // The launcher is as tall as its fill-in form (up to 80 % of the work area), like the real window.
        Window(name, window, 640, 420, beforeCapture: fitForm
            ? w => w.Height = Placement.Launcher(new PixelRect(0, 0, 1920, 1040), 1.0, ((SearchWindow)w).FormHeightDips()).Height
            : null);
    }

    // Stands in for the Acrylic backdrop (a bitmap can't show it): the translucent tints are drawn over bright and dark
    // stripes, so a layer that wrongly stays opaque, or text that loses contrast, shows up. Not a model of Acrylic's blur.
    private void SearchOverBackdrop(string name)
    {
        var resources = Application.Current.Resources;
        var opaque = resources["Brush.WindowBackdrop"];
        var glass = new SolidColorBrush(Color.FromArgb(0xC8, 0x80, 0x80, 0x80));
        resources["Brush.WindowBackdrop"] = glass; // BackdropActive reads a translucent brush as "backdrop on"
        var stripes = new LinearGradientBrush(new GradientStopCollection
        {
            new(Colors.Black, 0.0), new(Colors.Black, 0.25), new(Colors.White, 0.25), new(Colors.White, 0.5),
            new(Color.FromRgb(0x10, 0x20, 0xC0), 0.5), new(Color.FromRgb(0x10, 0x20, 0xC0), 0.75),
            new(Color.FromRgb(0xF0, 0xB0, 0x20), 0.75), new(Color.FromRgb(0xF0, 0xB0, 0x20), 1.0),
        }, new Point(0, 0), new Point(1, 0));
        _backdrop = stripes;
        try { Search(name, vm => vm.Query = "budg"); }
        finally
        {
            _backdrop = null;
            resources["Brush.WindowBackdrop"] = opaque;
        }
    }

    /// <summary>
    /// The search window's opening motion as frames (0, 33, 67, 100 and 167 ms), each drawn with the opacity and slide the
    /// real animation (SearchWindow.ShowAnimations) has at that moment, sampled through a seeked clock.
    /// </summary>
    private void SearchOpening(string name)
    {
        var host = NewHost(true);
        var vm = new SearchViewModel(new SearchActions(host.Library, host.Settings), () => DateTimeOffset.Now);
        var window = new SearchWindow(vm) { Width = 640, Height = 420 };
        vm.Reset();
        Place(window);
        window.Show();
        Pump();
        var (opacity, slide) = SearchWindow.ShowAnimations();
        foreach (var ms in new[] { 0, 33, 67, 100, 167 })
        {
            window.Body.Opacity = MotionTests.At(opacity, 1, ms);
            window.Slide.Y = MotionTests.At(slide, 0, ms);
            window.UpdateLayout();
            Capture($"{name}-{ms:000}ms", window);
        }
        window.Close();
    }

    private Brush? _backdrop;

    private void Toast(string name, string text, Core.Clipboard.ToastKind kind)
    {
        var toast = new ToastWindow();
        toast.SetContent(text, kind);
        Window(name, toast, null, null);
    }

    // ---- Rendering -----------------------------------------------------------------------------

    private void Window(string name, Window window, double? width, double? height, Action<Window>? beforeCapture = null)
    {
        if (width is { } w) window.Width = w;
        if (height is { } h) window.Height = h;
        Place(window);
        window.Show();
        Pump();
        if (beforeCapture is not null)
        {
            beforeCapture(window);
            Pump();
        }
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

    /// <summary>The real Send to menu (MainWindow.BuildSendMenu), drawn without opening it.</summary>
    private void SendMenu(string name, MainWindow window)
    {
        var menu = window.BuildSendMenu();
        menu.DataContext = window.DataContext;
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
        // A window's ActualWidth/Height include the title bar and frame, which are not part of its visual tree: draw only
        // the client area (its root), or a grey band shows below and beside the content.
        if (element is Window shown && VisualTreeHelper.GetChildrenCount(shown) > 0
            && VisualTreeHelper.GetChild(shown, 0) is FrameworkElement client && client.ActualWidth > 0)
            element = client;
        double w = element.ActualWidth, h = element.ActualHeight;
        var margin = isWindow ? 0 : 12; // a little of the window colour around a loose popup
        foreach (var (folder, scale) in Scales.Where(s => s.Name != "200" || AtTwoHundred.Contains(name)))
        {
            var dir = Path.Combine(_out, $"{_theme}-{folder}");
            Directory.CreateDirectory(dir);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(_backdrop ?? (Brush)Application.Current.Resources["Brush.Window"], null, new Rect(0, 0, w + 2 * margin, h + 2 * margin));
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

using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PromptLibrary.App.Theming;
using PromptLibrary.App.ViewModels;
using PromptLibrary.App.Views;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.App.Tests;

/// <summary>Typing into a choice field (<c>{relaxed / packed}</c>) must act like typing into a text box.</summary>
[Collection("WpfApplication")]
public class ChoiceTypingTests
{
    private sealed class Tokens : ITokenValues
    {
        public string? Clipboard => null;
        public DateTime Now => new(2026, 11, 3, 14, 5, 0);
        public CultureInfo Culture => CultureInfo.GetCultureInfo("en-US");
    }

    internal static void StartApp()
    {
        typeof(Application).GetField("_resourceAssembly", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, typeof(App).Assembly);
        if (Application.Current is null)
        {
            // Application's constructor queues OnStartup, which would take the single-instance mutex and start the whole
            // AppHost (tray, hotkey, the real library) or, while the real app is running, shut this Application down.
            // Tests only want App's resources, so cancel that queued startup.
            DispatcherOperation? startup = null;
            void Capture(object? sender, DispatcherHookEventArgs e) => startup ??= e.Operation;
            Dispatcher.CurrentDispatcher.Hooks.OperationPosted += Capture;
            try { new App().InitializeComponent(); }
            finally { Dispatcher.CurrentDispatcher.Hooks.OperationPosted -= Capture; }
            startup?.Abort();
        }
        ThemeManager.Current.Apply("light");
    }

    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) yield return hit;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    private static (FillInViewModel Vm, FillInPanel Panel, Window Window, TextBox Box) Open(string text = "A {relaxed / packed} trip")
    {
        StartApp();
        var vm = new FillInViewModel(text, new AppSettings(), new Tokens());
        var panel = new FillInPanel { DataContext = vm };
        var window = new Window { Content = panel, Width = 560, Height = 480, Left = -10000, Top = 0, ShowActivated = false, WindowStyle = WindowStyle.None };
        window.Show();
        Sta.DoEvents();
        var box = Descendants<TextBox>(panel).Single(t => t.IsVisible);
        return (vm, panel, window, box);
    }

    // What one typed letter does to a text box: it replaces the selection (if any) at the caret.
    private static void Type(TextBox box, char letter)
    {
        box.SelectedText = letter.ToString();
        if (box.SelectedText == letter.ToString() && box.SelectionStart + 1 == box.Text.Length) box.Select(box.Text.Length, 0); // the caret moves past the letter
        Sta.DoEvents();
    }

    [Fact]
    public void TypingAChoiceValue_KeepsEveryLetter_AndDoesNotHang() => Sta.RunOnApp(() =>
    {
        var (vm, _, window, box) = Open();
        box.Focus();
        foreach (var letter in "packed")
            Type(box, letter);
        Assert.Equal("packed", box.Text);
        Assert.Equal("packed", vm.Fields[0].Value);
        window.Close();
    }, timeoutSeconds: 20);

    [Fact]
    public void TypingTheStartOfAnOption_CompletesTheRestSelected_AndALetterReplacesIt() => Sta.RunOnApp(() =>
    {
        var (vm, _, window, box) = Open("A {relaxed / packed / mixed} trip");
        Type(box, 'p');
        Assert.Equal("packed", box.Text);
        Assert.Equal((1, 5), (box.SelectionStart, box.SelectionLength));
        Assert.Equal("packed", vm.Fields[0].Value);
        Type(box, 'x');                                   // not an option: the tail is replaced
        Assert.Equal("px", box.Text);
        Assert.Equal("px", vm.Fields[0].Value);
        window.Close();
    }, timeoutSeconds: 20);

    [Fact]
    public void FreeTextInAChoiceField_IsKeptAsTyped() => Sta.RunOnApp(() =>
    {
        var (vm, _, window, box) = Open();
        foreach (var letter in "slow") Type(box, letter);
        Assert.Equal("slow", box.Text);
        Assert.Equal("slow", vm.Fields[0].Value);
        Assert.Equal("A slow trip", vm.PreviewText);
        window.Close();
    }, timeoutSeconds: 20);

    [Fact]
    public void Backspace_DeletesTheCompletedTail_WithoutCompletingAgain() => Sta.RunOnApp(() =>
    {
        var (vm, _, window, box) = Open();
        Type(box, 'p');                                   // "p" + selected "acked"
        box.SelectedText = "";                            // Backspace removes the selection
        Sta.DoEvents();
        Assert.Equal("p", box.Text);
        Assert.Equal("p", vm.Fields[0].Value);
        window.Close();
    }, timeoutSeconds: 20);

    [Fact]
    public void RememberedChoice_ShowsInTheBox() => Sta.RunOnApp(() =>
    {
        StartApp();
        var settings = new AppSettings();
        settings.PlaceholderValues["relaxed / packed"] = "Packed";
        var vm = new FillInViewModel("A {relaxed / packed} trip", settings, new Tokens());
        var panel = new FillInPanel { DataContext = vm };
        var window = new Window { Content = panel, Left = -10000, Top = 0, ShowActivated = false, WindowStyle = WindowStyle.None };
        window.Show();
        Sta.DoEvents();
        Assert.Equal("Packed", Descendants<TextBox>(panel).Single(t => t.IsVisible).Text);
        window.Close();
    }, timeoutSeconds: 20);
}

internal static class AutomationNames
{
    public static string AutomationProperties_Name(this DependencyObject d) => System.Windows.Automation.AutomationProperties.GetName(d);
}

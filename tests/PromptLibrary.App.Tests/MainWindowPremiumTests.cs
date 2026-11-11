using System.IO;
using PromptLibrary.App.Team;
using PromptLibrary.App.Updates;
using PromptLibrary.App.ViewModels;
using PromptLibrary.Core.Backup;
using PromptLibrary.Core.Editing;
using PromptLibrary.Core.Exchange;
using PromptLibrary.Core.History;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.App.Tests;

/// <summary>
/// The main window's premium list and rail: delete a category with its prompts, ranked search with highlights, Recent,
/// category emoji, import and export, Send without the tray message, and refreshing pins from outside.
/// </summary>
public sealed class MainWindowPremiumTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pl-mainwin-" + Guid.NewGuid().ToString("N"));

    public MainWindowPremiumTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private sealed record World(MainViewModel Vm, FakeHost Host, FakeDialogs Dialogs, PromptHistoryStore History,
        Category Coding, Category Email, List<Prompt> Prompts);

    private World Build(params (string Title, string Description, string Text, string[] Tags, bool InEmail)[] prompts)
    {
        var host = new FakeHost();
        var editor = new LibraryEditor(host.Library, () => DateTimeOffset.Now);
        var coding = editor.AddCategory("Coding");
        var email = editor.AddCategory("Email");
        var created = new List<Prompt>();
        foreach (var p in prompts)
            created.Add(editor.AddPrompt((p.InEmail ? email : coding).Id, p.Title, p.Description, p.Text, p.Tags, null));
        var history = new PromptHistoryStore(Path.Combine(_root, "History"));
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(host, dialogs, history, _ => { });
        return new World(vm, host, dialogs, history, coding, email, created);
    }

    private static (string, string, string, string[], bool) P(string title, string description = "", string text = "body",
        string[]? tags = null, bool email = false) => (title, description, text, tags ?? [], email);

    // ---- Delete a category with its prompts ----------------------------------------------------

    [Fact]
    public void DeleteCategory_AlsoDeletingPrompts_RemovesThemTheirPinsUsageAndHistory() => Sta.Run(() =>
    {
        var w = Build(P("One"), P("Two"), P("Three"), P("Mail", email: true));
        var doomed = w.Prompts.Take(3).ToList();
        foreach (var p in doomed) w.History.Append(p.Id, new Prompt { Id = p.Id, Title = "old", Text = "older" }, p);
        w.Host.Settings.PinnedPromptIds.Add(doomed[0].Id);
        w.Host.Settings.Usage[doomed[1].Id] = new PromptUsage { Count = 2, LastUsedAt = DateTimeOffset.Now };
        w.Dialogs.Deletion = new CategoryDeletion(null, DeletePrompts: true);
        w.Vm.SelectedPrompt = w.Vm.Prompts.First(r => r.Id == doomed[0].Id);

        w.Vm.DeleteCategoryCommand.Execute(w.Vm.Categories.Single(c => c.Id == w.Coding.Id));

        Assert.Equal(("Coding", 3), w.Dialogs.LastDeletionAsked);
        Assert.Equal(["Mail"], w.Host.Library.Prompts.Select(p => p.Title));
        Assert.DoesNotContain(w.Host.Library.Categories, c => c.Id == w.Coding.Id);
        Assert.All(doomed, p => Assert.Empty(w.History.List(p.Id)));
        Assert.Empty(w.Host.Settings.PinnedPromptIds);
        Assert.Empty(w.Host.Settings.Usage);
        Assert.Null(w.Vm.Editor); // the open prompt went with its category
        Assert.Equal("Deleted \"Coding\" and 3 prompts.", w.Vm.StatusText);
        Assert.Equal(1, w.Host.LibrarySaves);
        Assert.Empty(w.Dialogs.Messages);
    });

    [Fact]
    public void DeleteCategory_MovingThePrompts_IsAsBefore() => Sta.Run(() =>
    {
        var w = Build(P("One"), P("Two"));
        w.Dialogs.Deletion = new CategoryDeletion(w.Email.Id, DeletePrompts: false);

        w.Vm.DeleteCategoryCommand.Execute(w.Vm.Categories.Single(c => c.Id == w.Coding.Id));

        Assert.Equal(2, w.Host.Library.Prompts.Count(p => p.CategoryId == w.Email.Id));
        Assert.Equal("Moved 2 prompts to Email.", w.Vm.StatusText);
    });

    [Fact]
    public void DeleteCategory_WithNoOtherCategory_CanStillDeleteThePrompts() => Sta.Run(() =>
    {
        var w = Build(P("One"));
        new LibraryEditor(w.Host.Library, () => DateTimeOffset.Now).DeleteCategory(w.Email.Id, null);
        w.Vm.RefreshAll();
        w.Dialogs.Deletion = new CategoryDeletion(null, DeletePrompts: true);

        w.Vm.DeleteCategoryCommand.Execute(w.Vm.Categories.Single(c => c.Id == w.Coding.Id));

        Assert.Empty(w.Host.Library.Prompts);
        Assert.Empty(w.Host.Library.Categories);
        Assert.Equal(0, w.Dialogs.TargetsOffered); // nobody to move them to
    });

    [Fact]
    public void DeleteCategory_Cancelled_ChangesNothing() => Sta.Run(() =>
    {
        var w = Build(P("One"));
        w.Dialogs.Deletion = null;

        w.Vm.DeleteCategoryCommand.Execute(w.Vm.Categories.Single(c => c.Id == w.Coding.Id));

        Assert.Single(w.Host.Library.Prompts);
        Assert.Contains(w.Host.Library.Categories, c => c.Id == w.Coding.Id);
    });

    [Fact]
    public void DeleteCategory_Empty_AsksPlainlyWithNoPromptChoice() => Sta.Run(() =>
    {
        var w = Build(P("Mail", email: true));

        w.Vm.DeleteCategoryCommand.Execute(w.Vm.Categories.Single(c => c.Id == w.Coding.Id));

        Assert.Null(w.Dialogs.LastDeletionAsked);
        Assert.Equal("Delete the category \"Coding\"?", w.Dialogs.LastConfirm);
        Assert.DoesNotContain(w.Host.Library.Categories, c => c.Id == w.Coding.Id);
    });

    // ---- Search --------------------------------------------------------------------------------

    [Fact]
    public void Search_RanksTitleHitsFirst_AndMarksTheMatchedLetters() => Sta.Run(() =>
    {
        var w = Build(
            P("Weekly report", "Summarise the week", "Write about refactor work"),
            P("Refactor this code", "Clean up", "x"),
            P("Zebra", "Unrelated", "nothing"));
        w.Vm.SelectedCategory = w.Vm.Categories.First(c => c.IsAll);

        w.Vm.SearchText = "refactor";

        Assert.Equal(["Refactor this code", "Weekly report"], w.Vm.Prompts.Select(r => r.Title));
        var first = w.Vm.Prompts[0];
        Assert.Contains(first.TitleSegments, s => s.Highlight && s.Text == "Refactor");
        Assert.Equal("Refactor this code", string.Concat(first.TitleSegments.Select(s => s.Text)));
    });

    [Fact]
    public void Search_UnderstandsTagAndCategoryFilters_AndWordOrderAndTypos() => Sta.Run(() =>
    {
        var w = Build(
            P("Review a pull request", tags: ["review"]),
            P("Explain code", tags: ["learning"]),
            P("Reply to client", "Polite reply", email: true));
        w.Vm.SelectedCategory = w.Vm.Categories.First(c => c.IsAll);

        w.Vm.SearchText = "#review";
        Assert.Equal(["Review a pull request"], w.Vm.Prompts.Select(r => r.Title));

        w.Vm.SearchText = "cat:email";
        Assert.Equal(["Reply to client"], w.Vm.Prompts.Select(r => r.Title));

        w.Vm.SearchText = "request pull";
        Assert.Equal(["Review a pull request"], w.Vm.Prompts.Select(r => r.Title));

        w.Vm.SearchText = "explian";
        Assert.Equal(["Explain code"], w.Vm.Prompts.Select(r => r.Title));
    });

    [Fact]
    public void Search_StaysInTheSelectedCategory_AndNoMatchesSaysSo() => Sta.Run(() =>
    {
        var w = Build(P("Alpha code"), P("Alpha mail", email: true));
        w.Vm.SelectedCategory = w.Vm.Categories.Single(c => c.Id == w.Coding.Id);

        w.Vm.SearchText = "alpha";
        Assert.Equal(["Alpha code"], w.Vm.Prompts.Select(r => r.Title));

        w.Vm.SearchText = "nothingmatches";
        Assert.True(w.Vm.ShowNoMatches);
        Assert.Equal("No prompts match \"nothingmatches\".", w.Vm.NoMatchesText);
    });

    [Fact]
    public void Row_DescriptionIsOneLine_AndTagsAreCappedAtThree() => Sta.Run(() =>
    {
        var w = Build(P("A", "First line\r\nsecond line", tags: ["a", "b", "c", "d"]));

        var row = w.Vm.Prompts.Single();

        Assert.Equal("First line", string.Concat(row.DescriptionSegments.Select(s => s.Text)));
        Assert.Equal(["a", "b", "c"], row.TagChips);
        Assert.True(row.HasDescription);
    });

    [Fact]
    public void TextSegments_SplitAndClip()
    {
        var parts = TextSegments.Split("Hello world", [(0, 2), (6, 100)]);

        Assert.Equal([("He", true), ("llo ", false), ("world", true)], parts.Select(s => (s.Text, s.Highlight)));
        Assert.Empty(TextSegments.Split("", [(0, 3)]));
        Assert.Equal([("plain", false)], TextSegments.Split("plain", null).Select(s => (s.Text, s.Highlight)));
    }

    // ---- Rail: Recent and emoji ------------------------------------------------------------------

    [Fact]
    public void Recent_IsHiddenUntilSomethingIsUsed_ThenListsLastUsedFirst_UpToTen() => Sta.Run(() =>
    {
        var w = Build(Enumerable.Range(1, 12).Select(i => P($"Prompt {i:00}")).ToArray());
        Assert.DoesNotContain(w.Vm.Categories, c => c.IsRecent);

        var now = DateTimeOffset.Now;
        for (var i = 0; i < 12; i++)
            w.Host.Settings.Usage[w.Prompts[i].Id] = new PromptUsage { Count = 1, LastUsedAt = now.AddMinutes(-i) };
        w.Vm.RefreshAll();

        var recent = w.Vm.Categories.Single(c => c.IsRecent);
        Assert.Equal(1, w.Vm.Categories.IndexOf(recent)); // right under All prompts
        Assert.Equal("Recent", recent.Name);
        Assert.Equal(10, recent.Count);

        w.Vm.SelectedCategory = recent;

        Assert.Equal(Enumerable.Range(1, 10).Select(i => $"Prompt {i:00}"), w.Vm.Prompts.Select(r => r.Title));
    });

    [Fact]
    public void Recent_FollowsACopy_WithoutTheUserDoingAnythingElse() => Sta.Run(() =>
    {
        var w = Build(P("One"), P("Two"));
        w.Vm.SelectedPrompt = w.Vm.Prompts.First(r => r.Title == "Two");

        w.Vm.CopyCommand.Execute(null);

        var recent = w.Vm.Categories.Single(c => c.IsRecent);
        Assert.Equal(1, recent.Count);
        w.Vm.SelectedCategory = recent;
        Assert.Equal(["Two"], w.Vm.Prompts.Select(r => r.Title));
    });

    [Fact]
    public void Recent_IsNotACategory_NoRenameNoDeleteNewPromptGoesToARealOne() => Sta.Run(() =>
    {
        var w = Build(P("One"));
        w.Host.Settings.Usage[w.Prompts[0].Id] = new PromptUsage { Count = 1, LastUsedAt = DateTimeOffset.Now };
        w.Vm.RefreshAll();
        var recent = w.Vm.Categories.Single(c => c.IsRecent);
        w.Vm.SelectedCategory = recent;

        w.Vm.StartRename(recent);
        w.Vm.DeleteCategory(recent);
        w.Vm.NewPromptCommand.Execute(null);

        Assert.False(recent.IsRenaming);
        Assert.Null(w.Dialogs.LastDeletionAsked);
        Assert.Equal(w.Coding.Id, w.Vm.Editor!.CategoryId);
    });

    [Fact]
    public void Rail_ShowsTheEmoji_AndRenamingCanChangeIt() => Sta.Run(() =>
    {
        var w = Build(P("One"));
        w.Coding.Emoji = "\U0001F4BB";
        w.Vm.RefreshAll();
        var item = w.Vm.Categories.Single(c => c.Id == w.Coding.Id);
        Assert.Equal("\U0001F4BB", item.Emoji);
        Assert.Null(w.Vm.Categories.Single(c => c.Id == w.Email.Id).Emoji);

        w.Vm.StartRename(item);
        Assert.Equal("\U0001F4BB", item.EditEmoji);
        item.EditEmoji = "\U0001F680";
        Assert.True(w.Vm.CommitRename(item)); // same name, new emoji: a change

        Assert.Equal("\U0001F680", w.Coding.Emoji);
        Assert.Equal("Coding", w.Coding.Name);
        Assert.Equal(1, w.Host.LibrarySaves);

        var again = w.Vm.Categories.Single(c => c.Id == w.Coding.Id);
        w.Vm.StartRename(again);
        again.EditEmoji = null; // "No emoji"
        Assert.True(w.Vm.CommitRename(again));
        Assert.Null(w.Coding.Emoji);
    });

    [Fact]
    public void NewCategory_CanCarryAnEmoji() => Sta.Run(() =>
    {
        var w = Build(P("One"));

        w.Vm.StartNewCategory();
        w.Vm.NewCategoryName = "Travel";
        w.Vm.NewCategoryEmoji = "✈️";
        Assert.True(w.Vm.CommitNewCategory());

        Assert.Equal("✈️", w.Host.Library.Categories.Single(c => c.Name == "Travel").Emoji);
        Assert.Equal("✈️", w.Vm.Categories.Single(c => c.Name == "Travel").Emoji);
        w.Vm.StartNewCategory();
        Assert.Null(w.Vm.NewCategoryEmoji); // a fresh editor starts without one
    });

    // ---- Import and export -----------------------------------------------------------------------

    [Fact]
    public void Export_ThenImportIntoAnotherLibrary_SkipsDuplicatesOnTheSecondRun() => Sta.Run(() =>
    {
        var source = Build(P("One", "d", "text one", ["x"]), P("Mail", "m", "text mail", email: true));
        var file = Path.Combine(_root, "export.json");
        source.Dialogs.Pick = new ExchangePick(file, IsFolder: false);

        source.Vm.ExportCommand.Execute(null);

        Assert.True(File.Exists(file));
        Assert.StartsWith("Exported 2 prompts", source.Vm.StatusText);

        var target = Build();
        target.Dialogs.Pick = new ExchangePick(file, IsFolder: false);
        target.Vm.ImportCommand.Execute(null);

        Assert.Equal("Imported 2 prompts.", target.Dialogs.Messages.Single());
        Assert.Equal(2, target.Host.Library.Prompts.Count);
        Assert.Equal(1, target.Host.LibrarySaves);
        Assert.Contains(target.Vm.Prompts, r => r.Title == "One" || r.Title == "Mail");

        target.Dialogs.Messages.Clear();
        target.Vm.ImportCommand.Execute(null);

        Assert.Equal("Imported 0 prompts. Skipped 2 duplicates.", target.Dialogs.Messages.Single());
        Assert.Equal(1, target.Host.LibrarySaves); // nothing changed, nothing saved
    });

    [Fact]
    public void ExportToAFolder_ThenImportThatFolder() => Sta.Run(() =>
    {
        var source = Build(P("One", text: "text one"), P("Mail", text: "text mail", email: true));
        var folder = Path.Combine(_root, "md");
        source.Dialogs.Pick = new ExchangePick(folder, IsFolder: true);
        source.Vm.ExportCommand.Execute(null);
        Assert.True(File.Exists(Path.Combine(folder, "Coding", "One.md")));

        var target = Build();
        target.Dialogs.Pick = new ExchangePick(folder, IsFolder: true);
        target.Vm.ImportCommand.Execute(null);

        Assert.Equal("Imported 2 prompts.", target.Dialogs.Messages.Single());
        Assert.Equal("text mail", target.Host.Library.Prompts.Single(p => p.Title == "Mail").Text);
    });

    [Fact]
    public void Import_OfSomethingThatIsNotAnExport_SaysWhy_AndChangesNothing() => Sta.Run(() =>
    {
        var w = Build(P("One"));
        var bad = Path.Combine(_root, "bad.json");
        File.WriteAllText(bad, "{ not json");
        w.Dialogs.Pick = new ExchangePick(bad, IsFolder: false);

        w.Vm.ImportCommand.Execute(null);

        var failure = Assert.Single(w.Dialogs.LastImport!.Failures);
        Assert.Equal(("bad.json", "not valid JSON"), (failure.File, failure.Reason));
        Assert.Equal("Imported 0 prompts. Skipped 1 file that couldn't be used.", w.Dialogs.Messages.Single());
        Assert.Single(w.Host.Library.Prompts);
        Assert.Equal(0, w.Host.LibrarySaves);
    });

    [Fact]
    public void Export_WithNoPrompts_SaysSo() => Sta.Run(() =>
    {
        var w = Build();
        w.Dialogs.Pick = new ExchangePick(Path.Combine(_root, "none.json"), IsFolder: false);

        w.Vm.ExportCommand.Execute(null);

        Assert.Equal("There are no prompts to export yet.", w.Dialogs.Messages.Single());
        Assert.False(File.Exists(Path.Combine(_root, "none.json")));
    });

    // ---- Copy, Send and the fill-in -----------------------------------------------------------------

    [Fact]
    public void Send_DoesNotAlsoShowTheTrayMessage_ButCopyKeepsItsOwn() => Sta.Run(() =>
    {
        var w = Build(P("One", text: "hello"));
        w.Vm.SelectedPrompt = w.Vm.Prompts.Single();

        w.Vm.SendCommand.Execute(null);
        w.Vm.CopyCommand.Execute(null);

        Assert.Equal([("hello", false), ("hello", true)], w.Host.Copies);
    });

    [Fact]
    public void FillIn_SendSaysSend_CopySaysCopy_AndTheTitleComesAlong() => Sta.Run(() =>
    {
        var w = Build(P("Letter", text: "Dear {name}"));
        w.Vm.SelectedPrompt = w.Vm.Prompts.Single();
        var labels = new List<string>();
        w.Vm.FillInResolver = t => { labels.Add($"{w.Vm.FillInPrimaryLabel}/{w.Vm.FillInTitle}"); return t.Replace("{name}", "Ann"); };

        w.Vm.CopyCommand.Execute(null);
        w.Vm.SendCommand.Execute(null);

        Assert.Equal(["Copy/Letter", "Send/Letter"], labels);
        Assert.Equal([("Dear Ann", true), ("Dear Ann", false)], w.Host.Copies);
    });

    [Fact]
    public void Tokens_AreFilledAtTheMomentOfTheCopy_AndCursorIsRemoved() => Sta.Run(() =>
    {
        var w = Build(P("Dated", text: "On {date}: {clipboard}{cursor}!"));
        w.Vm.SelectedPrompt = w.Vm.Prompts.Single();
        w.Vm.TokenSource = _ => new FixedTokens("from the clipboard");

        w.Vm.CopyCommand.Execute(null);

        Assert.Equal("On 11/11/2026: from the clipboard!", w.Host.Copies.Single().Text);
    });

    // ---- Refreshing from outside -------------------------------------------------------------------

    [Fact]
    public void UserStateChanged_RefreshesPinGlyphsAndTheRecentRow() => Sta.Run(() =>
    {
        var w = Build(P("One"), P("Two"));
        Assert.All(w.Vm.Prompts, r => Assert.False(r.IsPinned));

        w.Host.Settings.PinnedPromptIds.Add(w.Prompts[1].Id); // the search window pinned it
        w.Host.Settings.Usage[w.Prompts[0].Id] = new PromptUsage { Count = 1, LastUsedAt = DateTimeOffset.Now };
        w.Host.RaiseUserStateChanged();

        Assert.True(w.Vm.Prompts.Single(r => r.Title == "Two").IsPinned);
        Assert.False(w.Vm.Prompts.Single(r => r.Title == "One").IsPinned);
        Assert.Contains(w.Vm.Categories, c => c.IsRecent);
    });

    [Fact]
    public void UserStateChanged_LeavesAnOpenRenameAlone() => Sta.Run(() =>
    {
        var w = Build(P("One"));
        var item = w.Vm.Categories.Single(c => c.Id == w.Coding.Id);
        w.Vm.StartRename(item);
        item.EditName = "Typing";

        w.Host.Settings.Usage[w.Prompts[0].Id] = new PromptUsage { Count = 1, LastUsedAt = DateTimeOffset.Now };
        w.Host.RaiseUserStateChanged();

        Assert.Same(item, w.Vm.Categories.Single(c => c.Id == w.Coding.Id));
        Assert.True(item.IsRenaming);
        Assert.Equal("Typing", item.EditName);
    });

    // ---- Empty states, starter prompts, no-field prompts, editor meta ---------------------------------

    [Fact]
    public void EmptyLibrary_HasOneEmptyState_TheListsAndTheEditorStaysQuiet() => Sta.Run(() =>
    {
        var w = Build();

        Assert.True(w.Vm.ShowNoPromptsYet);
        Assert.Equal("", w.Vm.NoEditorText); // the list column says it; the editor doesn't repeat it
        Assert.True(w.Vm.HasNoEditor);
    });

    [Fact]
    public void NoPromptOpen_InALibraryWithPrompts_ShowsAFaintHint() => Sta.Run(() =>
    {
        var w = Build(P("One"));
        w.Vm.SelectedPrompt = null;

        Assert.Equal("Select a prompt, or create one.", w.Vm.NoEditorText);
    });

    [Fact]
    public void NoMatches_HeadlineHasNoFullStop()
    {
        Sta.Run(() =>
        {
            var w = Build(P("Alpha"));
            w.Vm.SelectedCategory = w.Vm.Categories.First(c => c.IsAll);
            w.Vm.SearchText = "zzz";
            Assert.Equal("No prompts match \"zzz\"", w.Vm.NoMatchesHeadline);
        });
    }

    [Fact]
    public void AddStarterPrompts_FillsAnEmptyLibrary_AndPressingTwiceAddsNothingMore() => Sta.Run(() =>
    {
        var w = Build();

        w.Vm.AddStarterPromptsCommand.Execute(null);

        Assert.Equal(39, w.Host.Library.Prompts.Count);
        Assert.Equal("Added 39 starter prompts.", w.Vm.StatusText);
        Assert.False(w.Vm.ShowNoPromptsYet);
        Assert.Equal(1, w.Host.LibrarySaves);
        Assert.Contains(w.Host.Library.Categories, c => c.Name == "Travel");

        var categories = w.Host.Library.Categories.Count;
        w.Vm.AddStarterPromptsCommand.Execute(null);

        Assert.Equal(39, w.Host.Library.Prompts.Count);
        Assert.Equal(categories, w.Host.Library.Categories.Count);
        Assert.Equal(1, w.Host.LibrarySaves); // nothing changed, nothing saved
        Assert.Equal("All the starter prompts are already in your library.", w.Vm.StatusText);
    });

    [Fact]
    public void AddStarterPrompts_SkipsTitlesAlreadyThere_AndUsesTheCategoryOfTheSameName() => Sta.Run(() =>
    {
        var w = Build(P("Plan a trip itinerary", text: "my own version"));
        var travel = new LibraryEditor(w.Host.Library, () => DateTimeOffset.Now).AddCategory("Travel");
        w.Vm.RefreshAll();

        w.Vm.AddStarterPromptsCommand.Execute(null);

        Assert.Equal(1, w.Host.Library.Prompts.Count(p => p.Title == "Plan a trip itinerary"));
        Assert.Equal("my own version", w.Host.Library.Prompts.Single(p => p.Title == "Plan a trip itinerary").Text);
        Assert.Equal(1, w.Host.Library.Categories.Count(c => c.Name == "Travel"));
        Assert.True(w.Host.Library.Prompts.Count(p => p.CategoryId == travel.Id) >= 2);
        Assert.Equal(39, w.Host.Library.Prompts.Count); // the 38 missing ones plus the one that was there
    });

    [Fact]
    public void ViewToggle_IsOnlyOfferedWhenThereIsSomethingToPreview() => Sta.Run(() =>
    {
        var w = Build(
            P("Plain", text: "Summarise this article in three bullet points, plainly."),
            P("Code", text: "Explain what a for loop does in C#, with one short example."),
            P("Fields", text: "Dear {name}, thanks for {reason}."),
            P("Markdown", text: "# Plan\n\n- one\n- two"));

        bool Toggle(string title)
        {
            w.Vm.SelectedPrompt = w.Vm.Prompts.Single(r => r.Title == title);
            return w.Vm.Editor!.ShowViewToggle;
        }

        Assert.False(Toggle("Plain"));
        Assert.False(Toggle("Code"));
        Assert.True(Toggle("Fields"));
        Assert.True(Toggle("Markdown"));

        w.Vm.SelectedPrompt = w.Vm.Prompts.Single(r => r.Title == "Plain");
        w.Vm.Editor!.IsPreview = true; // never hide the way back
        Assert.True(w.Vm.Editor.ShowViewToggle);
        w.Vm.Editor.IsPreview = false;
        w.Vm.Editor.Text = "now with {a field}";
        Assert.True(w.Vm.Editor.ShowViewToggle);
    });

    [Theory]
    [InlineData("Just a sentence. Not markdown at all.", false)]
    [InlineData("Use C# like a == b # not a heading", false)]
    [InlineData("2 * 3 * 4 = 24", false)]
    [InlineData("# Title\ntext", true)]
    [InlineData("  - item", true)]
    [InlineData("1. first", true)]
    [InlineData("Some **bold** words", true)]
    [InlineData("```js\nx\n```", true)]
    [InlineData("", false)]
    public void MarkdownHint_RecognisesWhatThePreviewDraws(string text, bool expected)
        => Assert.Equal(expected, MarkdownHint.Looks(text));

    [Fact]
    public void Editor_MetaText_SaysWhenUpdatedAndHowOftenUsed() => Sta.Run(() =>
    {
        var w = Build(P("One"));
        w.Prompts[0].UpdatedAt = DateTimeOffset.Now.AddDays(-2);
        w.Vm.RefreshAll();
        w.Vm.NewPromptCommand.Execute(null); // any other editor, so choosing the row opens the prompt afresh
        w.Vm.SelectedPrompt = w.Vm.Prompts.Single();
        Assert.Equal("Updated 2 days ago", w.Vm.Editor!.MetaText);

        w.Host.Settings.Usage[w.Prompts[0].Id] = new PromptUsage { Count = 5, LastUsedAt = DateTimeOffset.Now };
        w.Vm.NewPromptCommand.Execute(null); // any other editor, so choosing the row opens the prompt afresh
        w.Vm.SelectedPrompt = w.Vm.Prompts.Single();
        Assert.Equal("Updated 2 days ago · Used 5 times", w.Vm.Editor!.MetaText);

        w.Vm.CopyCommand.Execute(null);
        Assert.Equal("Updated 2 days ago · Used 6 times", w.Vm.Editor!.MetaText);

        w.Vm.NewPromptCommand.Execute(null);
        Assert.Equal("", w.Vm.Editor!.MetaText); // a new prompt has no history yet
    });

    // ---- Fakes ---------------------------------------------------------------------------------------

    private sealed class FixedTokens(string clipboard) : ITokenValues
    {
        public string? Clipboard => clipboard;
        public DateTime Now => new(2026, 11, 11, 9, 30, 0);
        public System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.GetCultureInfo("en-US");
    }

    private sealed class FakeHost : IMainHost
    {
        public UpdateCoordinator? Updates => null;
        public TeamLibraryService? Team => null;
        public Library Library { get; } = new();
        public AppSettings Settings { get; } = new();
        public string? BackupWarning => null;
        public string? BackupWarningDetail => null;
        public bool LibraryUnwritten => false;
        public int LibrarySaves { get; private set; }
        public List<(string Text, bool Tray)> Copies { get; } = [];

        public event EventHandler? BackupStateChanged { add { } remove { } }
        public event EventHandler? UserStateChanged;
        public void RaiseUserStateChanged() => UserStateChanged?.Invoke(this, EventArgs.Empty);

        public bool SaveLibrary() { LibrarySaves++; return true; }
        public bool SaveLibraryChange() { LibrarySaves++; return true; }
        public bool SaveSettings() => true;
        public void RefreshQuickMenu() { }
        public bool CopyText(string text, string title) => CopyText(text, title, notifyInTray: true);
        public bool CopyText(string text, string title, bool notifyInTray) { Copies.Add((text, notifyInTray)); return true; }
        public Task<BackupResult?> RunBackupAsync(BackupTrigger trigger, bool showInTray = true) => Task.FromResult<BackupResult?>(null);
        public void OpenRestore() { }
        public void OpenSettings(bool focusBackups = false) { }
        public void OpenOnboarding() { }
        public void ShowAbout() { }
        public void ExitApp(ExitReason reason = ExitReason.User) { }
    }

    private sealed class FakeDialogs : IDialogs
    {
        public List<string> Messages { get; } = [];
        public string? LastConfirm { get; private set; }
        public CategoryDeletion? Deletion { get; set; }
        public (string Name, int Count)? LastDeletionAsked { get; private set; }
        public int TargetsOffered { get; private set; } = -1;
        public ExchangePick? Pick { get; set; }

        public bool? AskSaveChanges(string question) => null;

        public bool ConfirmDestructive(string message, string action)
        {
            LastConfirm = message;
            return true;
        }

        public CategoryDeletion? ChooseCategoryDeletion(string categoryName, int promptCount, IReadOnlyList<Category> targets)
        {
            LastDeletionAsked = (categoryName, promptCount);
            TargetsOffered = targets.Count;
            return Deletion;
        }

        public ExchangePick? ChooseImportSource() => Pick;
        public ExchangePick? ChooseExportTarget() => Pick;
        public void ShowMessage(string message) => Messages.Add(message);
        public ImportResult? LastImport { get; private set; }
        public void ShowImportResults(ImportResult result) { LastImport = result; Messages.Add(result.Summary); }
        public bool ShowTeamHistory(TeamLibraryService service, bool review) => false;
    }
}

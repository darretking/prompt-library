using PromptLibrary.Core.History;
using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Tests.History;

public sealed class PromptHistoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PromptLibraryTests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);
    private readonly Guid _id = Guid.NewGuid();

    public PromptHistoryStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private PromptHistoryStore Store() => new(Path.Combine(_dir, "History"));

    private Prompt P(string title = "T", string desc = "D", string text = "X", int minutes = 0, params string[] tags)
        => new() { Id = _id, Title = title, Description = desc, Text = text, Tags = [.. tags], UpdatedAt = T.AddMinutes(minutes) };

    [Fact]
    public void Append_StoresThePreviousVersion_WithItsOwnTimestamp()
    {
        var store = Store();
        Assert.True(store.Append(_id, P("Old", "D", "old text", 5, "a"), P("New", "D", "new text", 9)));

        var v = Assert.Single(store.List(_id));
        Assert.Equal("Old", v.Title);
        Assert.Equal("old text", v.Text);
        Assert.Equal(["a"], v.Tags);
        Assert.Equal(T.AddMinutes(5), v.SavedAt);
    }

    [Theory]
    [InlineData("T2", "D", "X", "")]
    [InlineData("T", "D2", "X", "")]
    [InlineData("T", "D", "X2", "")]
    [InlineData("T", "D", "X", "newtag")]
    public void Append_WhenTitleDescriptionTextOrTagsChange_Stores(string title, string desc, string text, string tag)
    {
        var current = P(title, desc, text, 1, tag.Length == 0 ? [] : [tag]);
        Assert.True(Store().Append(_id, P(), current));
    }

    [Fact]
    public void Append_WhenNothingRelevantChanged_StoresNothing()
    {
        var store = Store();
        var current = P(minutes: 3);
        current.SendTo = "claude";
        current.CategoryId = Guid.NewGuid();
        Assert.False(store.Append(_id, P(), current));
        Assert.Empty(store.List(_id));
    }

    [Fact]
    public void Append_IgnoresLineEndingAndTagCaseDifferences()
        => Assert.False(Store().Append(_id, P(text: "a\nb", tags: "Tag"), P(text: "a\r\nb", tags: "tag")));

    [Fact]
    public void List_NewestFirst()
    {
        var store = Store();
        store.Append(_id, P("v1"), P("v2"));
        store.Append(_id, P("v2"), P("v3"));
        store.Append(_id, P("v3"), P("v4"));

        Assert.Equal(["v3", "v2", "v1"], store.List(_id).Select(v => v.Title));
    }

    [Fact]
    public void KeepsOnlyTheLast50()
    {
        var store = Store();
        for (var i = 0; i < 60; i++) store.Append(_id, P("v" + i), P("v" + (i + 1)));

        var list = store.List(_id);
        Assert.Equal(50, list.Count);
        Assert.Equal("v59", list[0].Title);
        Assert.Equal("v10", list[^1].Title);
    }

    [Fact]
    public void Delete_RemovesTheFile_AndIsHarmlessWhenThereIsNone()
    {
        var store = Store();
        store.Append(_id, P("a"), P("b"));
        Assert.True(store.Delete(_id));
        Assert.Empty(store.List(_id));
        Assert.True(store.Delete(_id));
        Assert.True(new PromptHistoryStore(Path.Combine(_dir, "never-created")).Delete(Guid.NewGuid()));
    }

    [Fact]
    public void HistoryIsPerPrompt()
    {
        var store = Store();
        var other = Guid.NewGuid();
        store.Append(_id, P("a"), P("b"));
        Assert.Empty(store.List(other));
    }

    [Fact]
    public void List_MissingOrCorruptFile_IsEmpty_AndAppendRecovers()
    {
        var folder = Path.Combine(_dir, "History");
        var store = new PromptHistoryStore(folder);
        Assert.Empty(store.List(_id));

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, _id.ToString("D") + ".json"), "{ not json");
        Assert.Empty(store.List(_id));
        Assert.True(store.Append(_id, P("a"), P("b")));
        Assert.Single(store.List(_id));
    }

    [Fact]
    public void IoFailure_ReturnsFalse_NeverThrows_AndLogs()
    {
        // The "folder" is a file, so nothing can be created under it.
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "x");
        var log = Path.Combine(_dir, "crash.log");
        var store = new PromptHistoryStore(blocker, log);

        Assert.False(store.Append(_id, P("a"), P("b")));
        Assert.Empty(store.List(_id));
        Assert.True(File.Exists(log));
    }

    [Fact]
    public void KeepsUnicodeAndMultilineText()
    {
        var store = Store();
        store.Append(_id, P(text: "line1\r\n\U0001F600 tab\tend"), P(text: "other"));
        Assert.Equal("line1\r\n\U0001F600 tab\tend", store.List(_id)[0].Text);
    }
}

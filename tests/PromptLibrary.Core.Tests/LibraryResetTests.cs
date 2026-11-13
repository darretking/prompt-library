using PromptLibrary.Core.Editing;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Seed;

namespace PromptLibrary.Core.Tests;

public class LibraryResetTests
{
    [Theory]
    [InlineData("DELETE", true)]
    [InlineData("delete", true)]
    [InlineData("  Delete  ", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("DELET", false)]
    [InlineData("DELETE.", false)]
    [InlineData("DELETE ALL", false)]
    [InlineData("D E L E T E", false)]
    [InlineData("yes", false)]
    public void IsConfirmed_OnlyTheWordItself(string? typed, bool expected)
        => Assert.Equal(expected, LibraryReset.IsConfirmed(typed));

    [Fact]
    public void ClearPersonal_RemovesEveryPromptAndCategory_AndReturnsTheirIds()
    {
        var lib = SeedLibrary.Create(DateTimeOffset.UtcNow);
        var ids = lib.Prompts.Select(p => p.Id).ToList();
        Assert.NotEmpty(ids);

        var removed = LibraryReset.ClearPersonal(lib, new AppSettings());

        Assert.Empty(lib.Prompts);
        Assert.Empty(lib.Categories);
        Assert.Equal(ids, removed);
        Assert.False(LibraryReset.HasAnything(lib));
        Assert.Equal(1, lib.SchemaVersion);
    }

    [Fact]
    public void ClearPersonal_KeepsTeamPinsUsageHiddenAndSeen_DropsPersonalOnes()
    {
        var lib = SeedLibrary.Create(DateTimeOffset.UtcNow);
        var mine = lib.Prompts[0].Id;
        var team = Guid.NewGuid(); // a team prompt: not in the personal library
        var s = new AppSettings
        {
            PinnedPromptIds = [mine, team],
            Usage = new() { [mine] = new PromptUsage { Count = 3 }, [team] = new PromptUsage { Count = 2 } },
            HiddenTeamPromptIds = [team],
            SeenTeamPrompts = new() { [team] = "abc" },
            TeamBaselineTaken = true,
            PlaceholderValues = new() { ["name"] = "Ana" },
        };

        LibraryReset.ClearPersonal(lib, s);

        Assert.Equal([team], s.PinnedPromptIds);
        Assert.Equal([team], s.Usage.Keys);
        Assert.Equal([team], s.HiddenTeamPromptIds);
        Assert.Equal("abc", s.SeenTeamPrompts[team]);
        Assert.True(s.TeamBaselineTaken);
        Assert.Equal("Ana", s.PlaceholderValues["name"]); // typed values aren't prompts
    }

    [Fact]
    public void ClearPersonal_OnAnEmptyLibrary_DoesNothing()
    {
        var lib = new Library();
        var s = new AppSettings { PinnedPromptIds = [Guid.NewGuid()] };

        Assert.False(LibraryReset.HasAnything(lib));
        Assert.Empty(LibraryReset.ClearPersonal(lib, s));
        Assert.Single(s.PinnedPromptIds);
    }

    [Fact]
    public void ClearPersonal_CategoriesWithoutPrompts_AreRemovedToo()
    {
        var lib = new Library { Categories = [new Category { Id = Guid.NewGuid(), Name = "Claude" }] };
        Assert.True(LibraryReset.HasAnything(lib));

        LibraryReset.ClearPersonal(lib, new AppSettings());

        Assert.Empty(lib.Categories);
    }
}

public sealed class LibraryResetOnDiskTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pl-reset-" + Guid.NewGuid().ToString("N"));

    public LibraryResetOnDiskTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void AnEmptiedLibrary_LoadsAsEmpty_SoTheStarterPromptsAreNotAddedBackAtTheNextStart()
    {
        // The app only seeds the starter prompts when library.json is missing; after "Delete all" it exists and is empty.
        var path = Path.Combine(_dir, "library.json");
        PromptLibrary.Core.Storage.JsonStore.SaveLibrary(path, new Library());

        var loaded = PromptLibrary.Core.Storage.JsonStore.LoadLibrary(path);

        Assert.Equal(PromptLibrary.Core.Storage.LoadStatus.Ok, loaded.Status);
        Assert.Empty(loaded.Value!.Prompts);
        Assert.Empty(loaded.Value.Categories);
    }
}

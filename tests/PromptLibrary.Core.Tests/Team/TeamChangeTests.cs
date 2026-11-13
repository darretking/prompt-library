using PromptLibrary.Core.History;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Team;

namespace PromptLibrary.Core.Tests.Team;

public class TeamChangeTests
{
    private static Prompt P(string title = "T", string desc = "D", string text = "a\nb") =>
        new() { Id = Guid.NewGuid(), Title = title, Description = desc, Text = text };

    private static TeamSeenSnapshot Old() => new("T", "D", "a\nb", "Email");

    [Fact]
    public void NoChange_IsEmpty()
    {
        var m = TeamChange.Between(Old(), P(), "Email");
        Assert.Empty(m.Fields);
        Assert.True(m.IsEmpty);
        Assert.All(m.TextDiff, l => Assert.Equal(DiffKind.Same, l.Kind));
    }

    [Theory]
    [InlineData("T2", "D", "Email", "Title")]
    [InlineData("T", "D2", "Email", "Description")]
    [InlineData("T", "D", "Sales", "Category")]
    public void SingleField_IsReported(string title, string desc, string cat, string name)
    {
        var m = TeamChange.Between(Old(), P(title, desc), cat);
        var f = Assert.Single(m.Fields);
        Assert.Equal(name, f.Name);
        Assert.False(m.TextChanged);
    }

    [Fact]
    public void FieldChange_CarriesOldAndNew()
    {
        var f = Assert.Single(TeamChange.Between(Old(), P("New"), "Email").Fields);
        Assert.Equal(new FieldChange("Title", "T", "New"), f);
    }

    [Fact]
    public void TextChange_GivesLineDiff()
    {
        var m = TeamChange.Between(Old(), P(text: "a\nc"), "Email");
        Assert.Empty(m.Fields);
        Assert.True(m.TextChanged);
        Assert.Contains(m.TextDiff, l => l.Kind == DiffKind.Removed && l.Text == "b");
        Assert.Contains(m.TextDiff, l => l.Kind == DiffKind.Added && l.Text == "c");
    }

    [Fact]
    public void TwoPrompts_Overload_Compares()
    {
        var m = TeamChange.Between(P("x"), "A", P("y", text: "z"), "B");
        Assert.Equal(["Title", "Category"], m.Fields.Select(f => f.Name));
        Assert.True(m.TextChanged);
    }

    [Fact]
    public void SnapshotAll_StoresEveryPromptWithCategoryName()
    {
        var lib = new Library();
        var cat = new Category { Id = Guid.NewGuid(), Name = "Email" };
        lib.Categories.Add(cat);
        var p = P();
        p.CategoryId = cat.Id;
        lib.Prompts.Add(p);
        var store = new TeamSeenStore(Path.Combine(Path.GetTempPath(), "pl-seen-" + Guid.NewGuid().ToString("N") + ".json"));

        store.SnapshotAll(lib);

        Assert.Equal(new TeamSeenSnapshot("T", "D", "a\nb", "Email"), store.Get(p.Id));
    }
}

using PromptLibrary.Core.Menu;
using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Tests;

public class QuickMenuBuilderTests
{
    [Fact]
    public void Build_SortsCategoriesAndPrompts_CaseInsensitive()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var lib = new Library();
        var names = new[] { "beta", "Alpha", "gamma" };
        for (var i = 0; i < 3; i++)
        {
            lib.Categories.Add(new Category { Id = ids[i], Name = names[i] });
            lib.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "z", CategoryId = ids[i] });
        }
        lib.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "b", CategoryId = ids[0] });
        lib.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "A", CategoryId = ids[0] });

        var menu = QuickMenuBuilder.Build(lib);

        Assert.Equal(new[] { "Alpha", "beta", "gamma" }, menu.Select(c => c.Name));
        var beta = menu.Single(c => c.Name == "beta");
        Assert.Equal(new[] { "A", "b", "z" }, beta.Prompts.Select(p => p.Title));
    }

    [Fact]
    public void Build_OmitsEmptyCategories()
    {
        var full = Guid.NewGuid();
        var lib = new Library();
        lib.Categories.Add(new Category { Id = full, Name = "Full" });
        lib.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Empty" });
        lib.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "p", CategoryId = full });

        var menu = QuickMenuBuilder.Build(lib);

        Assert.Equal(new[] { "Full" }, menu.Select(c => c.Name));
    }

    [Fact]
    public void Build_EmptyLibrary_ReturnsEmpty()
        => Assert.Empty(QuickMenuBuilder.Build(new Library()));
}

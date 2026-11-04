using PromptLibrary.Core.Models;
using PromptLibrary.Core.Seed;
using PromptLibrary.Core.Storage;
using PromptLibrary.Core.Text;

namespace PromptLibrary.Core.Tests;

public class SeedLibraryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Seed_Has12PromptsIn6Categories()
    {
        var lib = SeedLibrary.Create(Now);

        Assert.Equal(12, lib.Prompts.Count);
        Assert.Equal(
            new[] { "Coding", "Testing", "User Interface", "Marketing", "Documentation", "Code Review" },
            lib.Categories.Select(c => c.Name).ToArray());
        Assert.All(lib.Prompts, p => Assert.Contains(lib.Categories, c => c.Id == p.CategoryId));
        Assert.All(lib.Prompts, p => Assert.Equal(Now, p.CreatedAt));
        Assert.All(lib.Prompts, p => Assert.Equal(Now, p.UpdatedAt));
    }

    [Fact]
    public void Seed_FixThisError_HasEmptyDescription()
    {
        var prompt = SeedLibrary.Create(Now).Prompts.Single(p => p.Title == "Fix this error");

        Assert.Equal("", prompt.Description);
    }

    [Fact]
    public void Seed_WriteReadme_ContainsBashFence()
    {
        var prompt = SeedLibrary.Create(Now).Prompts.Single(p => p.Title == "Write a README");

        Assert.Contains("```bash", prompt.Text);
    }

    [Fact]
    public void Seed_EmptyStateCopy_ContainsEmoji()
    {
        var prompt = SeedLibrary.Create(Now).Prompts.Single(p => p.Title == "Write empty-state copy");

        Assert.Contains("\U0001F642", prompt.Text);
    }

    [Fact]
    public void Seed_RoundTripsThroughJsonStore()
    {
        var original = SeedLibrary.Create(Now);
        var path = Path.Combine(Path.GetTempPath(), $"seed-{Guid.NewGuid():N}.json");
        try
        {
            JsonStore.SaveLibrary(path, Copy(original));
            var result = JsonStore.LoadLibrary(path);

            Assert.Equal(LoadStatus.Ok, result.Status);
            var loaded = result.Value!;
            Assert.Equal(original.Categories.Select(c => (c.Id, c.Name)), loaded.Categories.Select(c => (c.Id, c.Name)));
            Assert.Equal(original.Prompts.Count, loaded.Prompts.Count);
            for (var i = 0; i < original.Prompts.Count; i++)
            {
                Assert.Equal(original.Prompts[i].Id, loaded.Prompts[i].Id);
                Assert.Equal(original.Prompts[i].Title, loaded.Prompts[i].Title);
                Assert.Equal(original.Prompts[i].CategoryId, loaded.Prompts[i].CategoryId);
                Assert.Equal(LineEndings.ToCrlf(original.Prompts[i].Description), loaded.Prompts[i].Description);
                Assert.Equal(LineEndings.ToCrlf(original.Prompts[i].Text), loaded.Prompts[i].Text);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    // SaveLibrary normalises its argument in place, so save a copy and keep the original untouched.
    private static Library Copy(Library source)
        => new()
        {
            SchemaVersion = source.SchemaVersion,
            Categories = source.Categories.Select(c => new Category { Id = c.Id, Name = c.Name }).ToList(),
            Prompts = source.Prompts.Select(p => new Prompt
            {
                Id = p.Id, Title = p.Title, Description = p.Description, CategoryId = p.CategoryId,
                Text = p.Text, CreatedAt = p.CreatedAt, UpdatedAt = p.UpdatedAt,
            }).ToList(),
        };
}

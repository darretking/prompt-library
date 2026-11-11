using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Editing;

/// <summary>Enforces the editing rules for categories and prompts on an in-memory <see cref="Library"/>.</summary>
public class LibraryEditor(Library library, Func<DateTimeOffset> now)
{
    public Category AddCategory(string name)
    {
        var trimmed = ValidateCategoryName(name, exceptId: null);
        var category = new Category { Id = Guid.NewGuid(), Name = trimmed };
        library.Categories.Add(category);
        return category;
    }

    public void RenameCategory(Guid id, string newName)
    {
        var category = FindCategory(id) ?? throw new ArgumentException("Category not found.", nameof(id));
        category.Name = ValidateCategoryName(newName, exceptId: id);
    }

    /// <summary>
    /// Deletes a category. If it holds prompts they are moved to <paramref name="movePromptsTo"/>,
    /// which must then be a different, existing category. Nothing changes if validation fails.
    /// </summary>
    /// <remarks>With <paramref name="deletePrompts"/> the prompts are removed instead of moved (<paramref name="movePromptsTo"/>
    /// is ignored). Returns the removed prompt ids (empty otherwise) so the caller can clear their pins, usage and history
    /// (see <see cref="PromptReferences"/>).</remarks>
    public IReadOnlyList<Guid> DeleteCategory(Guid id, Guid? movePromptsTo, bool deletePrompts = false)
    {
        var category = FindCategory(id) ?? throw new ArgumentException("Category not found.", nameof(id));

        if (deletePrompts)
        {
            var removed = library.Prompts.Where(p => p.CategoryId == id).Select(p => p.Id).ToList();
            library.Prompts.RemoveAll(p => p.CategoryId == id);
            library.Categories.Remove(category);
            return removed;
        }

        if (PromptCount(id) > 0)
        {
            if (movePromptsTo is not { } target || target == id || FindCategory(target) is null)
                throw new InvalidOperationException(
                    "Category contains prompts; choose a different existing category to move them to.");

            foreach (var prompt in library.Prompts.Where(p => p.CategoryId == id))
                prompt.CategoryId = target;
        }

        library.Categories.Remove(category);
        return [];
    }

    public int PromptCount(Guid categoryId) => library.Prompts.Count(p => p.CategoryId == categoryId);

    public Prompt AddPrompt(Guid categoryId, string title, string description, string text)
    {
        var (trimmedTitle, desc) = ValidatePrompt(categoryId, title, description, text);
        var timestamp = now();
        var prompt = new Prompt
        {
            Id = Guid.NewGuid(),
            Title = trimmedTitle,
            Description = desc,
            CategoryId = categoryId,
            Text = text,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };
        library.Prompts.Add(prompt);
        return prompt;
    }

    /// <summary>Adds a prompt with tags (normalised, see <see cref="TagRules"/>) and a default send target.</summary>
    public Prompt AddPrompt(Guid categoryId, string title, string description, string text,
        IEnumerable<string>? tags, string? sendTo)
    {
        var prompt = AddPrompt(categoryId, title, description, text);
        prompt.Tags = TagRules.Normalize(tags);
        prompt.SendTo = string.IsNullOrWhiteSpace(sendTo) ? null : sendTo;
        return prompt;
    }

    /// <summary>Updates a prompt's fields; its tags and send target stay as they are.</summary>
    public void UpdatePrompt(Guid id, string title, string description, Guid categoryId, string text)
    {
        var prompt = FindPrompt(id) ?? throw new ArgumentException("Prompt not found.", nameof(id));
        var (trimmedTitle, desc) = ValidatePrompt(categoryId, title, description, text);
        prompt.Title = trimmedTitle;
        prompt.Description = desc;
        prompt.CategoryId = categoryId;
        prompt.Text = text;
        prompt.UpdatedAt = now();
    }

    /// <summary>Updates a prompt's fields including tags (normalised) and default send target.</summary>
    public void UpdatePrompt(Guid id, string title, string description, Guid categoryId, string text,
        IEnumerable<string>? tags, string? sendTo)
    {
        UpdatePrompt(id, title, description, categoryId, text);
        var prompt = FindPrompt(id)!;
        prompt.Tags = TagRules.Normalize(tags);
        prompt.SendTo = string.IsNullOrWhiteSpace(sendTo) ? null : sendTo;
    }

    /// <summary>Sets or clears a category's emoji (null or blank clears).</summary>
    public void SetCategoryEmoji(Guid id, string? emoji)
    {
        var category = FindCategory(id) ?? throw new ArgumentException("Category not found.", nameof(id));
        category.Emoji = string.IsNullOrWhiteSpace(emoji) ? null : emoji.Trim();
    }

    public void DeletePrompt(Guid id)
    {
        var prompt = FindPrompt(id) ?? throw new ArgumentException("Prompt not found.", nameof(id));
        library.Prompts.Remove(prompt);
    }

    private Category? FindCategory(Guid id) => library.Categories.FirstOrDefault(c => c.Id == id);

    private Prompt? FindPrompt(Guid id) => library.Prompts.FirstOrDefault(p => p.Id == id);

    private string ValidateCategoryName(string name, Guid? exceptId)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new ArgumentException("Category name is required.", nameof(name));
        if (library.Categories.Any(c => c.Id != exceptId &&
                                        string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"A category named \"{trimmed}\" already exists.", nameof(name));
        return trimmed;
    }

    private (string Title, string Description) ValidatePrompt(Guid categoryId, string title, string? description, string text)
    {
        var trimmedTitle = title?.Trim();
        if (string.IsNullOrEmpty(trimmedTitle))
            throw new ArgumentException("Prompt title is required.", nameof(title));
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Prompt text is required.", nameof(text));
        if (FindCategory(categoryId) is null)
            throw new ArgumentException("Category not found.", nameof(categoryId));
        return (trimmedTitle, description ?? "");
    }
}

using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Editing;

/// <summary>
/// One prompt as it was before an edit (or "didn't exist yet" for a new prompt), so an edit that
/// was applied in memory but never written to disk can be undone when the user chooses Don't save.
/// </summary>
public sealed class PromptSnapshot
{
    private readonly Prompt? _before; // null: the prompt didn't exist before the edit

    private PromptSnapshot(Guid promptId, Prompt? before)
    {
        PromptId = promptId;
        _before = before;
    }

    public Guid PromptId { get; }

    /// <summary>Copies the current values of an existing prompt.</summary>
    public static PromptSnapshot Capture(Library library, Guid promptId)
    {
        var prompt = library.Prompts.FirstOrDefault(p => p.Id == promptId)
            ?? throw new ArgumentException("Prompt not found.", nameof(promptId));
        return new PromptSnapshot(promptId, new Prompt
        {
            Id = prompt.Id,
            Title = prompt.Title,
            Description = prompt.Description,
            CategoryId = prompt.CategoryId,
            Text = prompt.Text,
            CreatedAt = prompt.CreatedAt,
            UpdatedAt = prompt.UpdatedAt,
        });
    }

    /// <summary>A prompt that was just added; undoing removes it.</summary>
    public static PromptSnapshot ForNewPrompt(Guid promptId) => new(promptId, before: null);

    /// <summary>
    /// Puts the prompt back as it was (or removes it if it was new). If its old category has been
    /// deleted since, it stays in its current category so the library remains valid.
    /// </summary>
    public void Restore(Library library)
    {
        var prompt = library.Prompts.FirstOrDefault(p => p.Id == PromptId);
        if (prompt is null) return; // deleted meanwhile; nothing to undo

        if (_before is null)
        {
            library.Prompts.Remove(prompt);
            return;
        }

        prompt.Title = _before.Title;
        prompt.Description = _before.Description;
        prompt.Text = _before.Text;
        prompt.UpdatedAt = _before.UpdatedAt;
        if (library.Categories.Any(c => c.Id == _before.CategoryId))
            prompt.CategoryId = _before.CategoryId;
    }
}

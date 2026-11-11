namespace PromptLibrary.Core.Models;

/// <summary>The 36 emoji the category emoji picker offers (a 6 x 6 grid): work, writing, code, money, health, travel, food, kids, learning, ideas, chat, star and more.</summary>
public static class EmojiChoices
{
    public static IReadOnlyList<string> All { get; } =
    [
        "\U0001F4BC", "✍️", "\U0001F4BB", "\U0001F4B0", "\U0001FA7A", "✈️",
        "\U0001F37D️", "\U0001F9F8", "\U0001F4DA", "\U0001F4A1", "\U0001F4AC", "⭐",
        "\U0001F4DD", "\U0001F4E7", "\U0001F4CA", "\U0001F3AF", "\U0001F527", "\U0001F9E0",
        "\U0001F3A8", "\U0001F389", "\U0001F3E0", "\U0001F6D2", "\U0001F4C5", "\U0001F50D",
        "\U0001F680", "❤️", "\U0001F3AE", "⚖️", "\U0001F30D", "\U0001F52C",
        "\U0001F4E3", "\U0001F393", "\U0001F4AA", "\U0001F373", "\U0001F436", "\U0001F381",
    ];
}

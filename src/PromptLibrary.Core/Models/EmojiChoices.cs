namespace PromptLibrary.Core.Models;

/// <summary>
/// The 54 emoji the category emoji picker offers (a 6 x 9 grid): six rows of everyday topics (work, writing, code, money,
/// travel, kids, learning, ideas, chat, star and more), then three rows for business departments and functions (people and
/// HR, finance, growth, operations, IT, security, compliance, projects, records, product, logistics, manufacturing, the
/// company, customer support, communications, wins, strategy, and research and testing). Each must read on its tile in both
/// themes: the plate, stethoscope, graduation cap, busts, chess pawn, bank, microphone, receipt, newspaper and headphones
/// were dropped because they faded into light or dark tiles.
/// </summary>
public static class EmojiChoices
{
    public static IReadOnlyList<string> All { get; } =
    [
        "\U0001F4BC", "✍️", "\U0001F4BB", "\U0001F4B0", "\U0001F91D", "✈️",
        "\U0001F454", "\U0001F9F8", "\U0001F4DA", "\U0001F4A1", "\U0001F4AC", "⭐",
        "\U0001F4DD", "\U0001F4E7", "\U0001F4CA", "\U0001F3AF", "\U0001F527", "\U0001F9E0",
        "\U0001F3A8", "\U0001F389", "\U0001F3E0", "\U0001F6D2", "\U0001F4C5", "\U0001F50D",
        "\U0001F680", "❤️", "\U0001F3AE", "⚖️", "\U0001F30D", "\U0001F52C",
        "\U0001F4E3", "✅", "\U0001F4AA", "\U0001F373", "\U0001F436", "\U0001F381",
        // Business departments and functions: people / HR, finance, growth, operations, IT, security,
        "\U0001FAAA", "\U0001F9EE", "\U0001F4C8", "⚙️", "\U0001F5A5️", "\U0001F512",
        // compliance, projects, records, product, logistics, manufacturing,
        "\U0001F6E1️", "\U0001F4CB", "\U0001F5C2️", "\U0001F4E6", "\U0001F69A", "\U0001F3ED",
        // the company, customer support, communications, wins, strategy, research and testing.
        "\U0001F3E2", "\U0001F6DF", "☎️", "\U0001F3C6", "\U0001F9ED", "\U0001F9EA",
    ];
}

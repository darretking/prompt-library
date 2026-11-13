namespace PromptLibrary.App.Startup;

/// <summary>
/// The team file or link given on the command line (<c>PromptLibrary.exe &lt;path&gt;</c>, <c>--join &lt;path-or-url&gt;</c>)
/// and the check for a message coming in over the pipe. The result only prefills the Join dialog; nothing joins
/// without the user pressing Join.
/// </summary>
public static class JoinArguments
{
    /// <summary>The most a pipe message may hold, in UTF-8 bytes.</summary>
    public const int MaxBytes = 4096;

    /// <summary>The path or link to prefill, or null. Unknown switches are ignored.</summary>
    public static string? Parse(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--join", StringComparison.OrdinalIgnoreCase))
                return i + 1 < args.Count ? Validate(args[i + 1]) : null;
            if (arg.StartsWith("--join=", StringComparison.OrdinalIgnoreCase))
                return Validate(arg["--join=".Length..]);
            if (arg.StartsWith('-')) continue; // an unknown switch
            return Validate(arg);
        }
        return null;
    }

    /// <summary>The text when it is safe to show in the box (non-blank, ≤ 4 KB, no control characters), else null.</summary>
    public static string? Validate(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        if (System.Text.Encoding.UTF8.GetByteCount(message) > MaxBytes) return null;
        if (message.Any(char.IsControl)) return null;
        return message.Trim();
    }
}

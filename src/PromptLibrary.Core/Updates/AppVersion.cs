using System.Globalization;

namespace PromptLibrary.Core.Updates;

/// <summary>
/// A release version such as <c>1.0.1</c>, parsed from a GitHub tag (<c>v1.0.1</c>) or the app's assembly version.
/// Build metadata (<c>+…</c>) is ignored; a pre-release (<c>-beta.1</c>) sorts below the same version without one.
/// A 4th part only counts when it isn't 0, so the exe's <c>1.0.1.0</c> equals the tag <c>v1.0.1</c>.
/// </summary>
public readonly record struct AppVersion(int Major, int Minor, int Patch, int Revision, string? PreRelease)
    : IComparable<AppVersion>
{
    /// <summary>Parses <c>[v]major.minor[.patch[.revision]][-pre][+build]</c>; false for anything else (e.g. <c>latest</c>).</summary>
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim();
        if (s[0] is 'v' or 'V') s = s[1..];

        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];

        string? pre = null;
        var dash = s.IndexOf('-');
        if (dash >= 0)
        {
            pre = s[(dash + 1)..];
            s = s[..dash];
            if (pre.Length == 0) return false;
        }

        var parts = s.Split('.');
        if (parts.Length is < 2 or > 4) return false;

        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit)
                || !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }

        version = new AppVersion(numbers[0], numbers[1], numbers[2], numbers[3], pre);
        return true;
    }

    /// <summary>The running app's version, from its assembly (<c>1.0.1.0</c> → <c>1.0.1</c>).</summary>
    public static AppVersion FromSystemVersion(Version version)
        => new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0), null);

    public int CompareTo(AppVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        if (c == 0) c = Revision.CompareTo(other.Revision);
        if (c != 0) return c;

        // Same numbers: a release beats its pre-releases.
        if (PreRelease is null) return other.PreRelease is null ? 0 : 1;
        if (other.PreRelease is null) return -1;
        return string.CompareOrdinal(PreRelease, other.PreRelease);
    }

    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;
    public static bool operator >=(AppVersion a, AppVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(AppVersion a, AppVersion b) => a.CompareTo(b) <= 0;

    public override string ToString()
    {
        var text = $"{Major}.{Minor}.{Patch}";
        if (Revision != 0) text += $".{Revision}";
        if (PreRelease is not null) text += $"-{PreRelease}";
        return text;
    }
}

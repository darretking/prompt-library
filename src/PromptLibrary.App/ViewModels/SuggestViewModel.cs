using System.Runtime.InteropServices;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.ViewModels;

/// <summary>
/// The "Suggest for the team" dialog: the prompt's title (read only), a team category (the team's own plus the prompt's
/// category name), an optional note (at most 280 characters) and the sender's name (prefilled, may be blank).
/// </summary>
public sealed class SuggestViewModel : ObservableObject
{
    private string _note = "";

    public SuggestViewModel(string promptTitle, string ownCategoryName, IEnumerable<string> teamCategoryNames, string? yourName)
    {
        PromptTitle = promptTitle;
        var names = teamCategoryNames.Select(n => n.Trim()).Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var own = ownCategoryName.Trim();
        var match = names.FirstOrDefault(n => string.Equals(n, own, StringComparison.OrdinalIgnoreCase));
        if (match is null && own.Length > 0) names.Add(own);
        Categories = names;
        SelectedCategory = match ?? names.FirstOrDefault() ?? own;
        YourName = yourName ?? "";
    }

    public string PromptTitle { get; }

    public IReadOnlyList<string> Categories { get; }

    public string SelectedCategory { get; set; }

    public int MaxNoteLength => TeamSuggestions.MaxNote;

    public string Note
    {
        get => _note;
        set
        {
            if (SetProperty(ref _note, value)) OnPropertyChanged(nameof(CountText));
        }
    }

    public string CountText => $"{_note.Length} / {MaxNoteLength}";

    public string YourName { get; set; }

    /// <summary>True once Send suggestion was chosen.</summary>
    public bool Sent { get; private set; }

    public bool Send()
    {
        if (_note.Length > MaxNoteLength || string.IsNullOrWhiteSpace(SelectedCategory)) return false;
        Sent = true;
        return true;
    }

    /// <summary>The name Windows shows for this user, or the account name when that isn't available.</summary>
    public static string DefaultName()
    {
        try
        {
            var size = 0;
            GetUserNameEx(3 /* NameDisplay */, null, ref size);
            if (size > 1)
            {
                var sb = new StringBuilder(size);
                if (GetUserNameEx(3, sb, ref size) && sb.ToString().Trim() is { Length: > 0 } name) return name;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException) { }
        return Environment.UserName;
    }

    [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserNameEx(int nameFormat, StringBuilder? name, ref int size);
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PromptLibrary.Core.Team;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.App.ViewModels;

/// <summary>One row of the fill-in values list: a field name and the value everyone starts with.</summary>
public sealed partial class FillInValueRow : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _value = "";

    /// <summary>The name as the fill-in form labels it: first letter upper-case ("client" shows as "Client").</summary>
    public string Label => Name.Length == 0 ? Name : char.ToUpperInvariant(Name[0]) + Name[1..];

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Label));

    /// <summary>Fields found in team prompts have a fixed name; rows added by hand can be named.</summary>
    public bool NameIsEditable { get; init; }
}

/// <summary>
/// The "Fill-in values for everyone" dialog: Field | Value rows, prefilled with every field name team prompts use
/// (A to Z) plus the values already stored. An empty value is not stored. Save checks the limits
/// (<see cref="TeamFile.MaxFillInValues"/> values, <see cref="TeamFile.MaxFillInValueLength"/> characters each) and says so plainly.
/// </summary>
public sealed class TeamFillInValuesViewModel : ObservableObject
{
    private string? _message;

    public TeamFillInValuesViewModel(IEnumerable<string> promptTexts, IReadOnlyDictionary<string, string> existing)
    {
        var names = new List<string>();
        void Add(string name)
        {
            name = name.Trim();
            if (name.Length > 0 && !PromptTemplate.IsToken(name) && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                names.Add(name);
        }
        foreach (var text in promptTexts)
            foreach (var field in PromptTemplate.Parse(text).Where(f => f.Kind == TemplateFieldKind.Text))
                Add(field.Name);
        foreach (var key in existing.Keys) Add(key);

        foreach (var name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var value = existing.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
            Rows.Add(new FillInValueRow { Name = name, Value = value ?? "" });
        }
    }

    public ObservableCollection<FillInValueRow> Rows { get; } = [];

    public int MaxValueLength => TeamFile.MaxFillInValueLength;

    /// <summary>A plain sentence under the list when Save was refused; null otherwise.</summary>
    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>The values to store: filled rows only, trimmed. Set by a successful <see cref="TrySave"/>.</summary>
    public IReadOnlyDictionary<string, string> Result { get; private set; } = new Dictionary<string, string>();

    /// <summary>Add a field: a new empty row to name.</summary>
    public FillInValueRow AddField()
    {
        var row = new FillInValueRow { NameIsEditable = true };
        Rows.Add(row);
        return row;
    }

    /// <summary>Checks the rows; true (and <see cref="Result"/> set) when they can be saved.</summary>
    public bool TrySave()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows)
        {
            var value = row.Value.Trim();
            if (value.Length == 0) continue;
            var name = row.Name.Trim();
            if (name.Length == 0)
            {
                Message = "Give each value a field name, or clear the value.";
                return false;
            }
            if (value.Length > TeamFile.MaxFillInValueLength)
            {
                Message = $"\"{name}\" is too long. Values can be up to {TeamFile.MaxFillInValueLength} characters.";
                return false;
            }
            if (!result.TryAdd(name, value))
            {
                Message = $"\"{name}\" is listed twice. Keep one.";
                return false;
            }
        }
        if (result.Count > TeamFile.MaxFillInValues)
        {
            Message = $"You can store up to {TeamFile.MaxFillInValues} values. Clear some and try again.";
            return false;
        }
        Message = null;
        Result = result;
        return true;
    }
}

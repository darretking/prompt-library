using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PromptLibrary.App.Team;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.ViewModels;

/// <summary>One of the user's own categories in the "Copy some of your categories in" list.</summary>
public sealed class TeamSeedChoice(Category category) : ObservableObject
{
    private bool _isChecked;

    public Guid Id => category.Id;
    public string Name => category.Name;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetProperty(ref _isChecked, value)) Toggled?.Invoke(this, EventArgs.Empty);
        }
    }

    internal event EventHandler? Toggled;
}

/// <summary>
/// The Start a team library dialog: a folder and a name, an optional (collapsed) list of the user's categories to
/// copy in, and "Add example business prompts", which is on by default while no category is copied in (until the
/// user decides it themselves). When the folder already has a team library nothing is overwritten: the dialog says
/// so and offers <b>Join it</b>. All file work runs in the background.
/// </summary>
public sealed class TeamStartViewModel : ObservableObject
{
    private readonly Func<string, string, IReadOnlyCollection<Guid>, bool, Task<TeamCreateResult>> _start;
    private readonly Func<string, Task<TeamJoinResult>> _join;
    private readonly Func<string, string?> _providerOf;
    private string _folder = "";
    private string _name = "";
    private bool _addExamples = true;
    private bool _examplesChosen;
    private bool _isWorking;
    private string? _error;
    private string? _existingFile;

    public TeamStartViewModel(IEnumerable<Category> categories,
        Func<string, string, IReadOnlyCollection<Guid>, bool, Task<TeamCreateResult>> start,
        Func<string, Task<TeamJoinResult>> join, Func<string, string?> providerOf)
    {
        _start = start;
        _join = join;
        _providerOf = providerOf;
        Categories = categories.Select(c => new TeamSeedChoice(c)).ToList();
        foreach (var choice in Categories) choice.Toggled += (_, _) => OnCategoryToggled();
        StartCommand = new AsyncRelayCommand(StartAsync, () => CanStart);
        JoinItCommand = new AsyncRelayCommand(JoinItAsync);
    }

    /// <summary>The library was started (or the existing one joined); the dialog closes.</summary>
    public event EventHandler? Finished;

    public IReadOnlyList<TeamSeedChoice> Categories { get; }
    public bool HasCategories => Categories.Count > 0;
    public IAsyncRelayCommand StartCommand { get; }
    public IAsyncRelayCommand JoinItCommand { get; }

    public string Folder
    {
        get => _folder;
        set
        {
            if (SetProperty(ref _folder, value)) Changed();
        }
    }

    public string TeamName
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value)) Changed();
        }
    }

    /// <summary>The business example prompts. Setting it is the user's choice; after that it no longer follows the checklist.</summary>
    public bool AddBusinessExamples
    {
        get => _addExamples;
        set
        {
            _examplesChosen = true;
            SetProperty(ref _addExamples, value);
        }
    }

    public bool IsWorking
    {
        get => _isWorking;
        private set
        {
            if (!SetProperty(ref _isWorking, value)) return;
            OnPropertyChanged(nameof(CanEdit));
            StartCommand.NotifyCanExecuteChanged();
        }
    }

    public bool CanEdit => !_isWorking;

    public bool CanStart => !_isWorking && !string.IsNullOrWhiteSpace(_folder) && !string.IsNullOrWhiteSpace(_name);

    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => _error is not null;

    /// <summary><b>Join it</b> shows when the folder already holds a team library.</summary>
    public bool CanJoinExisting => _existingFile is not null;

    public async Task StartAsync()
    {
        if (!CanStart) return;
        var folder = _folder.Trim().Trim('"');
        var ids = Categories.Where(c => c.IsChecked).Select(c => c.Id).ToList();
        SetExisting(null);
        Error = null;
        IsWorking = true;
        try
        {
            if (!await Task.Run(() => Directory.Exists(folder)))
            {
                Error = TeamDialogText.Describe(TeamDialogText.CantReachFolder, await Task.Run(() => _providerOf(folder)));
                return;
            }

            var result = await _start(folder, _name.Trim(), ids, _addExamples);
            if (result.Created)
            {
                Finished?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (result.ExistingName is { Length: > 0 } existing)
            {
                SetExisting(result.FilePath);
                Error = $"This folder already has a team library ({existing}).";
                return;
            }
            Error = result.Message ?? "Couldn't start the team library.";
        }
        catch (Exception)
        {
            Error = "Couldn't start the team library.";
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>Joins the library that is already in the folder instead.</summary>
    public async Task JoinItAsync()
    {
        if (_existingFile is not { } file || _isWorking) return;
        Error = null;
        IsWorking = true;
        try
        {
            var result = await _join(file);
            if (result.Success)
            {
                Finished?.Invoke(this, EventArgs.Empty);
                return;
            }
            SetExisting(null);
            Error = result.Message ?? "Couldn't join that team library.";
        }
        catch (Exception)
        {
            SetExisting(null);
            Error = "Couldn't join that team library.";
        }
        finally
        {
            IsWorking = false;
        }
    }

    private void OnCategoryToggled()
    {
        if (_examplesChosen) return;
        var any = Categories.Any(c => c.IsChecked);
        if (_addExamples == !any) return;
        _addExamples = !any;
        OnPropertyChanged(nameof(AddBusinessExamples));
    }

    private void Changed()
    {
        Error = null;
        SetExisting(null);
        StartCommand.NotifyCanExecuteChanged();
    }

    private void SetExisting(string? file)
    {
        _existingFile = file;
        OnPropertyChanged(nameof(CanJoinExisting));
    }
}

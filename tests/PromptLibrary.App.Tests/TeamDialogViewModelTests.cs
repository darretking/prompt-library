using System.IO;
using System.Net.Http;
using PromptLibrary.App.Team;
using PromptLibrary.App.ViewModels;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.Tests;

public sealed class TeamDialogViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pl-team-dlg-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = new();
    private readonly AppSettings _settings = new();

    public TeamDialogViewModelTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        _http.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private TeamLibraryService NewService(Library? personal = null)
    {
        var mine = personal ?? new Library();
        return new(_http, "1.0.0", Path.Combine(_root, "cache", "team-cache.json"), _settings, () => mine, () => true,
            () => new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero), _ => { }, TimeSpan.FromMilliseconds(50));
    }

    private string MakeTeamFolder(string name)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        var team = new TeamLibrary { Name = name };
        File.WriteAllText(Path.Combine(folder, TeamFile.FileName), TeamFile.Serialize(team));
        return folder;
    }

    private static T Wait<T>(Task<T> task) { Wait((Task)task); return task.Result; }

    private static void Wait(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < until) Sta.DoEvents();
        task.GetAwaiter().GetResult();
    }

    private static TeamJoinViewModel JoinVm(Func<string, Task<TeamJoinResult>> join, string? provider = null)
        => new(join, _ => provider);

    [Fact]
    public void Join_UnreachableFolder_ShowsProviderHint() => Sta.Run(() =>
    {
        var vm = JoinVm(_ => Task.FromResult(new TeamJoinResult(false, "Can't reach that folder.")), provider: "OneDrive");
        vm.Input = @"C:\Nowhere";

        Wait(vm.JoinAsync());

        Assert.Equal("Can't reach that folder. Is OneDrive running?", vm.Error);
        Assert.True(vm.HasError);
        Assert.False(vm.IsChecking);
    });

    [Fact]
    public void Join_UnreachableFolder_NoProvider_ShowsPlainMessage() => Sta.Run(() =>
    {
        var vm = JoinVm(_ => Task.FromResult(new TeamJoinResult(false, "Can't reach that folder.")));
        vm.Input = @"C:\Nowhere";

        Wait(vm.JoinAsync());

        Assert.Equal("Can't reach that folder.", vm.Error);
    });

    [Fact]
    public void Join_OtherMessages_PassThroughExactly() => Sta.Run(() =>
    {
        var vm = JoinVm(_ => Task.FromResult(new TeamJoinResult(false, "That isn't a Prompt Library team file.")), "OneDrive");
        vm.Input = "x";

        Wait(vm.JoinAsync());

        Assert.Equal("That isn't a Prompt Library team file.", vm.Error);
    });

    [Fact]
    public void Join_ShowsCheckingWhileItReads_AndLocksTheBox() => Sta.Run(() =>
    {
        var gate = new TaskCompletionSource<TeamJoinResult>();
        var vm = JoinVm(_ => gate.Task);
        vm.Input = "x";

        var running = vm.JoinAsync();

        Assert.True(vm.IsChecking);
        Assert.False(vm.CanEdit);
        Assert.False(vm.CanJoin);
        gate.SetResult(new TeamJoinResult(false, "That isn't a Prompt Library team file."));
        Wait(running);
        Assert.False(vm.IsChecking);
        Assert.True(vm.CanEdit);
    });

    [Fact]
    public void Join_NothingTyped_CantJoin() => Sta.Run(() =>
    {
        var calls = 0;
        var vm = JoinVm(_ => { calls++; return Task.FromResult(new TeamJoinResult(true, null)); });

        Wait(vm.JoinAsync());

        Assert.False(vm.CanJoin);
        Assert.Equal(0, calls);
    });

    [Fact]
    public void Join_Success_RaisesJoined_AndTypingClearsAnError() => Sta.Run(() =>
    {
        var ok = false;
        var vm = JoinVm(_ => Task.FromResult(ok ? new TeamJoinResult(true, null) : new TeamJoinResult(false, "That isn't a Prompt Library team file.")));
        var joined = 0;
        vm.Joined += (_, _) => joined++;
        vm.Input = "x";
        Wait(vm.JoinAsync());
        Assert.NotNull(vm.Error);

        vm.Input = "y";
        Assert.Null(vm.Error);
        ok = true;
        Wait(vm.JoinAsync());

        Assert.Equal(1, joined);
    });

    [Fact]
    public void Join_RealService_BadFile_ShowsNotATeamFile() => Sta.Run(() =>
    {
        var folder = Path.Combine(_root, "bad");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, TeamFile.FileName), "not json");
        using var svc = NewService();
        var vm = JoinVm(svc.JoinAsync);
        vm.Input = folder;

        Wait(vm.JoinAsync());

        Assert.Equal("That isn't a Prompt Library team file.", vm.Error);
        Assert.Null(svc.Source);
    });

    [Fact]
    public void Start_ExistingLibrary_OffersJoinIt_AndJoinItJoinsIt() => Sta.Run(() =>
    {
        var folder = MakeTeamFolder("acme");
        using var svc = NewService();
        var vm = new TeamStartViewModel([], svc.StartAsync, svc.JoinAsync, _ => null) { Folder = folder, TeamName = "Other" };
        var finished = 0;
        vm.Finished += (_, _) => finished++;

        Wait(vm.StartAsync());

        Assert.Equal("This folder already has a team library (acme).", vm.Error);
        Assert.True(vm.CanJoinExisting);
        Assert.Null(svc.Source);

        Wait(vm.JoinItAsync());

        Assert.Equal(1, finished);
        Assert.Equal("acme", svc.Team!.Name);
    });

    [Fact]
    public void Start_NewFolder_CreatesAndJoins_WithBusinessExamples() => Sta.Run(() =>
    {
        var folder = Path.Combine(_root, "fresh");
        Directory.CreateDirectory(folder);
        using var svc = NewService();
        var vm = new TeamStartViewModel([], svc.StartAsync, svc.JoinAsync, _ => null) { Folder = folder, TeamName = "Acme" };
        var finished = 0;
        vm.Finished += (_, _) => finished++;

        Wait(vm.StartAsync());

        Assert.Equal(1, finished);
        Assert.Null(vm.Error);
        Assert.Equal("Acme", svc.Team!.Name);
        Assert.Equal(24, svc.Team.Content.Prompts.Count);
        Assert.Equal(6, svc.Team.Content.Categories.Count);
    });

    [Fact]
    public void Start_MissingFolder_SaysCantReach_WithProvider() => Sta.Run(() =>
    {
        var started = 0;
        var vm = new TeamStartViewModel([],
            (_, _, _, _) => { started++; return Task.FromResult(new TeamCreateResult(true, "", null, null)); },
            _ => Task.FromResult(new TeamJoinResult(true, null)), _ => "Google Drive")
        { Folder = Path.Combine(_root, "missing"), TeamName = "Acme" };

        Wait(vm.StartAsync());

        Assert.Equal("Can't reach that folder. Is Google Drive running?", vm.Error);
        Assert.Equal(0, started);
    });

    [Fact]
    public void Start_NeedsFolderAndName() => Sta.Run(() =>
    {
        var vm = new TeamStartViewModel([], (_, _, _, _) => Task.FromResult(new TeamCreateResult(true, "", null, null)),
            _ => Task.FromResult(new TeamJoinResult(true, null)), _ => null);
        Assert.False(vm.CanStart);
        vm.Folder = _root;
        Assert.False(vm.CanStart);
        vm.TeamName = "Acme";
        Assert.True(vm.CanStart);
    });

    [Fact]
    public void Examples_DefaultOn_TurnOffWhenACategoryIsCopied_UnlessChosen() => Sta.Run(() =>
    {
        var cats = new[] { new Category { Id = Guid.NewGuid(), Name = "Writing" }, new Category { Id = Guid.NewGuid(), Name = "Code" } };
        TeamStartViewModel Make() => new(cats, (_, _, _, _) => Task.FromResult(new TeamCreateResult(true, "", null, null)),
            _ => Task.FromResult(new TeamJoinResult(true, null)), _ => null);

        var vm = Make();
        Assert.True(vm.AddBusinessExamples);
        vm.Categories[0].IsChecked = true;
        Assert.False(vm.AddBusinessExamples);
        vm.Categories[0].IsChecked = false;
        Assert.True(vm.AddBusinessExamples);

        var chosen = Make();
        chosen.AddBusinessExamples = true; // the user's own decision stays
        chosen.Categories[1].IsChecked = true;
        Assert.True(chosen.AddBusinessExamples);
    });

    [Fact]
    public void Start_CopiesCheckedCategories_AndNoExamplesWhenOff() => Sta.Run(() =>
    {
        var folder = Path.Combine(_root, "seeded");
        Directory.CreateDirectory(folder);
        var writing = new Category { Id = Guid.NewGuid(), Name = "Writing" };
        var mine = new Library();
        mine.Categories.Add(writing);
        mine.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "Mine", Text = "Mine", CategoryId = writing.Id });
        using var svc = NewService(mine);
        var vm = new TeamStartViewModel(mine.Categories, svc.StartAsync, svc.JoinAsync, _ => null) { Folder = folder, TeamName = "Acme" };
        vm.Categories[0].IsChecked = true;

        Wait(vm.StartAsync());

        Assert.Null(vm.Error);
        Assert.Single(svc.Team!.Content.Prompts);
        Assert.Equal("Mine", svc.Team.Content.Prompts[0].Title);
    });

    [Fact]
    public void Connection_NotConnected_ShowsJoinAndStart_Connected_ShowsOnlyLeave() => Sta.Run(() =>
    {
        var folder = MakeTeamFolder("acme");
        using var svc = NewService();
        using var vm = new TeamConnectionViewModel(svc);
        Assert.True(vm.IsAvailable);
        Assert.True(vm.ShowActions);
        Assert.False(vm.IsConnected);

        Wait(svc.JoinAsync(folder));
        Sta.DoEvents();

        Assert.False(vm.ShowActions);
        Assert.True(vm.IsConnected);
        Assert.Equal("Connected to acme", vm.ConnectedLine);
        Assert.Equal(folder, vm.SourceText);
        Assert.Equal("Leave acme? Team prompts will disappear; your copies stay.", vm.LeaveQuestion);

        vm.Leave();

        Assert.True(vm.ShowActions);
        Assert.False(vm.IsConnected);
        Assert.Null(_settings.TeamSource);
    });

    [Fact]
    public void Connection_NoService_HidesTheSection()
    {
        using var vm = new TeamConnectionViewModel(null);
        Assert.False(vm.IsAvailable);
        Assert.False(vm.ShowActions);
    }
}

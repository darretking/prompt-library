using System.IO;
using System.Net.Http;
using PromptLibrary.App.Startup;
using PromptLibrary.App.Team;
using PromptLibrary.App.ViewModels;
using PromptLibrary.Core.Backup;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.Tests;

public sealed class JoinStartupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pl-join-" + Guid.NewGuid().ToString("N"));

    public JoinStartupTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void Parse_takes_a_bare_path_a_join_switch_or_a_url()
    {
        Assert.Equal(@"C:\Team\team-prompts.json", JoinArguments.Parse([@"C:\Team\team-prompts.json"]));
        Assert.Equal(@"\srv\share", JoinArguments.Parse(["--join", @"\srv\share"]));
        Assert.Equal("https://example.com/t.json", JoinArguments.Parse(["https://example.com/t.json"]));
        Assert.Equal("https://example.com/t.json", JoinArguments.Parse(["--join=https://example.com/t.json"]));
    }

    [Fact]
    public void Parse_ignores_unknown_switches_and_empty_input()
    {
        Assert.Null(JoinArguments.Parse([]));
        Assert.Null(JoinArguments.Parse(["--unknown"]));
        Assert.Null(JoinArguments.Parse(["--join"]));
        Assert.Null(JoinArguments.Parse(["--join", "  "]));
        Assert.Equal(@"C:\a", JoinArguments.Parse(["--minimized", @"C:\a"]));
    }

    [Fact]
    public void Validate_rejects_long_messages_and_control_characters()
    {
        Assert.Equal("ok", JoinArguments.Validate(" ok "));
        Assert.Null(JoinArguments.Validate(new string('a', JoinArguments.MaxBytes + 1)));
        Assert.NotNull(JoinArguments.Validate(new string('a', JoinArguments.MaxBytes)));
        Assert.Null(JoinArguments.Validate("a\nb"));
        Assert.Null(JoinArguments.Validate("a\rb"));
        Assert.Null(JoinArguments.Validate("a\0b"));
        Assert.Null(JoinArguments.Validate(null));
    }

    private sealed class FakePreset(string? value) : ITeamPresetSource
    {
        public string? Read() => value;
    }

    private static (Func<string, Task<TeamJoinResult>> Join, List<string> Calls) FakeJoin(bool success)
    {
        var calls = new List<string>();
        return (s => { calls.Add(s); return Task.FromResult(new TeamJoinResult(success, null)); }, calls);
    }

    [Fact]
    public async Task Preset_joins_when_no_team_and_records_it()
    {
        var settings = new AppSettings();
        var saved = 0;
        var (join, calls) = FakeJoin(true);
        Assert.True(await TeamPreset.ApplyAsync(new FakePreset(@"\srv\Prompts"), settings, false, join, () => { saved++; return true; }));
        Assert.Equal([@"\srv\Prompts"], calls);
        Assert.Equal(@"\srv\Prompts", settings.PresetTeamSourceApplied);
        Assert.Equal(1, saved);
    }

    [Fact]
    public async Task Preset_is_skipped_when_applied_blank_or_already_in_a_team()
    {
        var (join, calls) = FakeJoin(true);
        var applied = new AppSettings { PresetTeamSourceApplied = "x" };
        Assert.False(await TeamPreset.ApplyAsync(new FakePreset("x"), applied, false, join, () => true));
        Assert.False(await TeamPreset.ApplyAsync(new FakePreset("  "), new AppSettings(), false, join, () => true));
        Assert.False(await TeamPreset.ApplyAsync(new FakePreset(null), new AppSettings(), false, join, () => true));
        Assert.False(await TeamPreset.ApplyAsync(new FakePreset("y"), new AppSettings(), true, join, () => true));
        Assert.Empty(calls);
        // A different value joins again.
        Assert.True(await TeamPreset.ApplyAsync(new FakePreset("z"), applied, false, join, () => true));
        Assert.Equal("z", applied.PresetTeamSourceApplied);
    }

    [Fact]
    public async Task Preset_failure_leaves_applied_unchanged()
    {
        var settings = new AppSettings { PresetTeamSourceApplied = "old" };
        var (join, _) = FakeJoin(false);
        Assert.False(await TeamPreset.ApplyAsync(new FakePreset("new"), settings, false, join, () => true));
        Assert.Equal("old", settings.PresetTeamSourceApplied);
        Assert.False(await TeamPreset.ApplyAsync(new FakePreset("new"), settings, false,
            _ => throw new IOException(), () => true));
        Assert.Equal("old", settings.PresetTeamSourceApplied);
    }

    [Fact]
    public async Task Join_dialog_lists_found_files_and_a_click_fills_the_box()
    {
        var hits = new List<FoundTeamFile> { new(@"C:\o\team-prompts.json", "Acme", @"OneDrive - Acme\Shared") };
        var vm = new TeamJoinViewModel(_ => Task.FromResult(new TeamJoinResult(true, null)), _ => null,
            _ => Task.FromResult<IReadOnlyList<FoundTeamFile>>(hits), initialInput: "prefilled");
        Assert.Equal("prefilled", vm.Input);
        Assert.False(vm.HasFound);
        await vm.FindAsync();
        Assert.True(vm.HasFound);
        Assert.Single(vm.Found);
        vm.PickFoundCommand.Execute(vm.Found[0]);
        Assert.Equal(@"C:\o\team-prompts.json", vm.Input);
    }

    [Fact]
    public async Task Join_dialog_without_hits_stays_hidden()
    {
        var vm = new TeamJoinViewModel(_ => Task.FromResult(new TeamJoinResult(true, null)), _ => null,
            _ => Task.FromResult<IReadOnlyList<FoundTeamFile>>([]));
        await vm.FindAsync();
        Assert.False(vm.HasFound);
        Assert.False(vm.IsLooking);
    }

    [Fact]
    public void Validate_rejects_format_characters_such_as_bidi_override()
    {
        Assert.Null(JoinArguments.Validate("C:\team‮gnp.json"));
        Assert.Null(JoinArguments.Validate("a​b"));
    }

    [Fact]
    public void Pipe_name_includes_the_session_id()
    {
        Assert.NotEqual(JoinPipe.NameFor("", 1), JoinPipe.NameFor("", 2));
        Assert.Contains("s7", JoinPipe.NameFor("-x", 7));
    }

    [Fact]
    public void Backoff_grows_and_stops_at_30_seconds()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(200), JoinPipe.Backoff(1));
        Assert.True(JoinPipe.Backoff(3) > JoinPipe.Backoff(2));
        Assert.Equal(TimeSpan.FromSeconds(30), JoinPipe.Backoff(50));
    }
}

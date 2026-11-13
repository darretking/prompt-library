using PromptLibrary.Core.Backup;
using PromptLibrary.Core.Team;

namespace PromptLibrary.Core.Tests.Team;

public sealed class TeamJoinHelpersTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private string Team(string relDir, string name = "Acme", string? content = null)
    {
        var dir = Path.Combine(_root, relDir);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, TeamFile.FileName);
        File.WriteAllText(file, content ?? TeamFile.Serialize(new TeamLibrary { Name = name }));
        return file;
    }

    private Task<IReadOnlyList<FoundTeamFile>> Find(Func<TimeSpan>? clock = null, TimeSpan? budget = null, int max = 50)
        => TeamFileFinder.FindAsync([_root], CancellationToken.None, clock, budget, max);

    [Fact]
    public async Task Finder_ReadsNameAndShortLocation_DepthLimit()
    {
        Team(@"a\b", "Deep2");
        Team(@"1\2\3\4\5", "Depth5");
        Team(@"1\2\3\4\5\6", "Depth6");
        var found = await Find();
        Assert.Contains(found, f => f.LibraryName == "Deep2"
            && f.ShortLocation == Path.Combine(Path.GetFileName(_root), "a", "b"));
        Assert.Contains(found, f => f.LibraryName == "Depth5");
        Assert.DoesNotContain(found, f => f.LibraryName == "Depth6");
    }

    [Fact]
    public async Task Finder_SkipsHiddenSystemNodeModulesGit()
    {
        Team("node_modules", "N");
        Team(".git", "G");
        Team("ok", "Ok");
        var hidden = Directory.CreateDirectory(Path.Combine(_root, "hid")).FullName;
        Team("hid", "H");
        File.SetAttributes(hidden, FileAttributes.Directory | FileAttributes.Hidden);
        var sys = Directory.CreateDirectory(Path.Combine(_root, "sys")).FullName;
        Team("sys", "S");
        File.SetAttributes(sys, FileAttributes.Directory | FileAttributes.System);
        try
        {
            var found = await Find();
            Assert.Equal(["Ok"], found.Select(f => f.LibraryName).ToArray());
        }
        finally
        {
            File.SetAttributes(hidden, FileAttributes.Directory);
            File.SetAttributes(sys, FileAttributes.Directory);
        }
    }

    [Fact]
    public async Task Finder_StopsAtMaxHits()
    {
        for (var i = 0; i < 60; i++) Team($"d{i}", $"T{i}");
        Assert.Equal(50, (await Find()).Count);
        Assert.Equal(3, (await Find(max: 3)).Count);
    }

    [Fact]
    public async Task Finder_StopsWhenBudgetSpent()
    {
        for (var i = 0; i < 10; i++) Team($"d{i}", $"T{i}");
        var found = await Find(() => TimeSpan.FromSeconds(4));
        Assert.Empty(found);

        var calls = 0;
        var some = await Find(() => calls++ < 8 ? TimeSpan.Zero : TimeSpan.FromSeconds(4));
        Assert.InRange(some.Count, 1, 9);
    }

    [Fact]
    public async Task Finder_SkipsUnreadableFile_AndMissingRoot_NeverThrows()
    {
        Team("bad", content: "not json {");
        Team("good", "Good");
        var found = await TeamFileFinder.FindAsync([Path.Combine(_root, "nope"), _root], CancellationToken.None);
        Assert.Equal(["Good"], found.Select(f => f.LibraryName).ToArray());
    }

    private static readonly CloudFolder Biz = new(CloudProvider.OneDriveBusiness, "OneDrive – Acme", @"C:\Users\x\OneDrive - Acme");

    [Fact]
    public void Invite_CloudFile_UsesFolderNameNotUserPath()
    {
        var text = TeamInvite.Build(new TeamSource(TeamSourceKind.File, @"C:\Users\x\OneDrive - Acme\Shared\Prompts\team-prompts.json"), [Biz]);
        Assert.Equal(
            "Join our team prompts in Prompt Library.\n"
            + "1. Get the app: https://github.com/darretking/prompt-library/releases/latest\n"
            + "2. In Prompt Library open Settings → Team prompts → Join, and paste:\n"
            + @"OneDrive - Acme\Shared\Prompts", text);
        Assert.DoesNotContain(@"C:\Users", text);
    }

    [Fact]
    public void Invite_FileInCloudRoot_IsJustFolderName()
        => Assert.EndsWith("paste:\nOneDrive - Acme",
            TeamInvite.Build(new TeamSource(TeamSourceKind.File, @"C:\Users\x\OneDrive - Acme\team-prompts.json"), [Biz]));

    [Fact]
    public void Invite_NetworkPath_And_Url()
    {
        var unc = @"\\srv\share\team\team-prompts.json";
        Assert.EndsWith("paste:\n" + unc, TeamInvite.Build(new TeamSource(TeamSourceKind.File, unc), [Biz]));
        var url = "https://example.com/t.json";
        Assert.EndsWith("paste:\n" + url, TeamInvite.Build(new TeamSource(TeamSourceKind.Url, url), [Biz]));
    }

    [Theory]
    [InlineData(CloudProvider.OneDrive, "OneDrive", @"onedrive\Shared")]
    [InlineData(CloudProvider.OneDriveBusiness, "OneDrive – Acme", @"OneDrive - Acme/Shared")]
    [InlineData(CloudProvider.GoogleDrive, "Google Drive", @"My Drive\Shared")]
    [InlineData(CloudProvider.Dropbox, "Dropbox", @"DROPBOX\Shared")]
    public void FromInput_ResolvesCloudRelative(CloudProvider p, string display, string input)
    {
        var rootName = p switch
        {
            CloudProvider.OneDriveBusiness => "OneDrive - Acme",
            CloudProvider.GoogleDrive => "My Drive",
            _ => display,
        };
        var root = Path.Combine(_root, rootName);
        var clouds = new[] { new CloudFolder(p, display, root) };
        var src = TeamSource.FromInput(input, clouds)!;
        Assert.Equal(Path.Combine(root, "Shared", TeamFile.FileName), src.Location);
    }

    [Fact]
    public void FromInput_NonMatching_Unchanged()
    {
        var clouds = new[] { new CloudFolder(CloudProvider.Dropbox, "Dropbox", Path.Combine(_root, "Dropbox")) };
        Assert.Equal(TeamSource.FromInput(@"DropboxFoo\x"), TeamSource.FromInput(@"DropboxFoo\x", clouds));
        Assert.Equal(TeamSource.FromInput(@"Other\x"), TeamSource.FromInput(@"Other\x", clouds));
        Assert.Equal(TeamSource.FromInput(@"Dropbox\x"), TeamSource.FromInput(@"Dropbox\x", null));
    }

    [Fact]
    public async Task Finder_SkipsOversizeTeamFile()
    {
        var big = TeamFile.Serialize(new TeamLibrary { Name = "Big" }) + new string(' ', TeamReader.MaxBytes);
        Team("big", "Big", big);
        Team("ok", "Ok");
        var found = await Find();
        Assert.DoesNotContain(found, f => f.LibraryName == "Big");
        Assert.Contains(found, f => f.LibraryName == "Ok");
    }

    [Fact]
    public async Task Finder_ChecksBudgetBeforeReadingAFile()
    {
        Team("", "Root");
        var found = await Find(() => TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(3));
        Assert.Empty(found);
    }
}

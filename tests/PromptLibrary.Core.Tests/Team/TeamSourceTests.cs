using PromptLibrary.Core.Team;

namespace PromptLibrary.Core.Tests.Team;

public class TeamSourceTests
{
    [Fact]
    public void FromInput_Folder_File_Https_Http_Junk()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var folder = TeamSource.FromInput(dir)!;
            Assert.Equal(TeamSourceKind.File, folder.Kind);
            Assert.Equal(Path.Combine(dir, "team-prompts.json"), folder.Location);

            var file = Path.Combine(dir, "mine.json");
            Assert.Equal(new TeamSource(TeamSourceKind.File, file), TeamSource.FromInput(file));

            Assert.Equal(new TeamSource(TeamSourceKind.Url, "https://example.com/t.json"), TeamSource.FromInput("https://example.com/t.json"));
            Assert.Null(TeamSource.FromInput("http://example.com/t.json"));
            Assert.Null(TeamSource.FromInput("ftp://example.com/t.json"));
            Assert.Null(TeamSource.FromInput(""));
            Assert.Null(TeamSource.FromInput("   "));
            Assert.Null(TeamSource.FromInput("https://"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("https://www.dropbox.com/s/abc/team-prompts.json?dl=0", "https://www.dropbox.com/s/abc/team-prompts.json?dl=1")]
    [InlineData("https://www.dropbox.com/s/abc/team-prompts.json", "https://www.dropbox.com/s/abc/team-prompts.json?dl=1")]
    [InlineData("https://dropbox.com/scl/fi/x/t.json?rlkey=k&dl=0", "https://dropbox.com/scl/fi/x/t.json?rlkey=k&dl=1")]
    [InlineData("https://www.dropbox.com/s/abc/t.json?dl=1", "https://www.dropbox.com/s/abc/t.json?dl=1")]
    public void FromInput_Dropbox_ForcesDirectDownload(string input, string expected)
        => Assert.Equal(expected, TeamSource.FromInput(input)!.Location);

    [Fact]
    public void FromInput_GitHubBlob_BecomesRaw()
    {
        Assert.Equal("https://raw.githubusercontent.com/acme/prompts/main/teams/team-prompts.json",
            TeamSource.FromInput("https://github.com/acme/prompts/blob/main/teams/team-prompts.json")!.Location);
        Assert.Equal("https://raw.githubusercontent.com/acme/prompts/main/t.json",
            TeamSource.FromInput("https://raw.githubusercontent.com/acme/prompts/main/t.json")!.Location);
    }
}

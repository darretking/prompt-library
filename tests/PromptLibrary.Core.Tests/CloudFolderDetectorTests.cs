using PromptLibrary.Core.Backup;

namespace PromptLibrary.Core.Tests;

public class CloudFolderDetectorTests
{
    private sealed class FakeProbe : ICloudProbe
    {
        public Dictionary<string, string> Env { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> AccountFolders { get; } = [];
        public List<string> Roots { get; } = [];
        public HashSet<string> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string UserProfile { get; set; } = @"C:\Users\x";

        public string? GetEnvironmentVariable(string name) => Env.GetValueOrDefault(name);
        public IEnumerable<string> GetOneDriveAccountFolders() => AccountFolders;
        public IEnumerable<string> GetDriveRoots() => Roots;
        public bool DirectoryExists(string path) => Existing.Contains(path);
    }

    [Fact]
    public void Detect_OneDrivePersonalFromEnv()
    {
        var p = new FakeProbe();
        p.Env["OneDrive"] = @"C:\Users\x\OneDrive";
        p.Existing.Add(@"C:\Users\x\OneDrive");

        var result = CloudFolderDetector.Detect(p);

        Assert.Equal([new CloudFolder(CloudProvider.OneDrive, "OneDrive", @"C:\Users\x\OneDrive")], result);
    }

    [Fact]
    public void Detect_BusinessFromEnvAndRegistry_NameFromFolder()
    {
        var p = new FakeProbe();
        p.Env["OneDriveCommercial"] = @"C:\Users\x\OneDrive - Contoso";
        p.AccountFolders.Add(@"C:\Users\x\OneDrive - Fabrikam");
        p.Existing.Add(@"C:\Users\x\OneDrive - Contoso");
        p.Existing.Add(@"C:\Users\x\OneDrive - Fabrikam");

        var result = CloudFolderDetector.Detect(p);

        Assert.Equal(
        [
            new CloudFolder(CloudProvider.OneDriveBusiness, "OneDrive \u2013 Contoso", @"C:\Users\x\OneDrive - Contoso"),
            new CloudFolder(CloudProvider.OneDriveBusiness, "OneDrive \u2013 Fabrikam", @"C:\Users\x\OneDrive - Fabrikam"),
        ], result);
    }

    [Fact]
    public void Detect_GoogleDriveVirtualDrive()
    {
        var p = new FakeProbe();
        p.Roots.Add(@"C:\");
        p.Roots.Add(@"G:\");
        p.Existing.Add(@"G:\My Drive");

        var result = CloudFolderDetector.Detect(p);

        Assert.Equal([new CloudFolder(CloudProvider.GoogleDrive, "Google Drive", @"G:\My Drive")], result);
    }

    [Fact]
    public void Detect_GoogleDriveOnOtherLetter()
    {
        var p = new FakeProbe();
        p.Roots.Add(@"H:\");
        p.Existing.Add(@"H:\My Drive");

        var result = CloudFolderDetector.Detect(p);

        Assert.Equal([new CloudFolder(CloudProvider.GoogleDrive, "Google Drive", @"H:\My Drive")], result);
    }

    [Fact]
    public void Detect_GoogleDriveFolderMount()
    {
        var p = new FakeProbe();
        p.Existing.Add(@"C:\Users\x\Google Drive\My Drive");
        p.Existing.Add(@"C:\Users\x\My Drive");

        var result = CloudFolderDetector.Detect(p);

        Assert.Equal(
        [
            new CloudFolder(CloudProvider.GoogleDrive, "Google Drive", @"C:\Users\x\Google Drive\My Drive"),
            new CloudFolder(CloudProvider.GoogleDrive, "Google Drive", @"C:\Users\x\My Drive"),
        ], result);
    }

    [Fact]
    public void Detect_IgnoresMissingFolders()
    {
        var p = new FakeProbe();
        p.Env["OneDrive"] = @"C:\Users\x\OneDrive";
        p.Env["OneDriveCommercial"] = @"C:\Users\x\OneDrive - Contoso";
        p.AccountFolders.Add(@"C:\Users\x\OneDrive - Gone");
        p.Roots.Add(@"G:\");

        Assert.Empty(CloudFolderDetector.Detect(p));
    }

    [Fact]
    public void Detect_DeduplicatesEnvAndRegistry()
    {
        var p = new FakeProbe();
        p.Env["OneDriveConsumer"] = @"C:\Users\x\OneDrive";
        p.Env["OneDrive"] = @"c:\users\x\onedrive\";
        p.AccountFolders.Add(@"C:\Users\x\OneDrive\");
        p.Env["OneDriveCommercial"] = @"C:\Users\x\OneDrive - Contoso";
        p.AccountFolders.Add(@"c:\users\x\ONEDRIVE - CONTOSO\");
        p.Existing.Add(@"C:\Users\x\OneDrive");
        p.Existing.Add(@"c:\users\x\onedrive\");
        p.Existing.Add(@"C:\Users\x\OneDrive\");
        p.Existing.Add(@"C:\Users\x\OneDrive - Contoso");
        p.Existing.Add(@"c:\users\x\ONEDRIVE - CONTOSO\");

        var result = CloudFolderDetector.Detect(p);

        Assert.Equal(2, result.Count);
        Assert.Equal(CloudProvider.OneDrive, result[0].Provider);
        Assert.Equal(CloudProvider.OneDriveBusiness, result[1].Provider);
    }

    [Fact]
    public void Detect_OneDriveEnvWithBusinessName_IsBusiness()
    {
        var p = new FakeProbe();
        p.Env["OneDrive"] = @"C:\Users\x\OneDrive - Contoso";
        p.Existing.Add(@"C:\Users\x\OneDrive - Contoso");

        var result = CloudFolderDetector.Detect(p);

        Assert.Equal([new CloudFolder(CloudProvider.OneDriveBusiness, "OneDrive \u2013 Contoso", @"C:\Users\x\OneDrive - Contoso")], result);
    }

    [Fact]
    public void Detect_Order()
    {
        var p = new FakeProbe();
        p.Roots.Add(@"G:\");
        p.Env["OneDriveCommercial"] = @"C:\Users\x\OneDrive - Zeta";
        p.AccountFolders.Add(@"C:\Users\x\OneDrive - alpha");
        p.Env["OneDrive"] = @"C:\Users\x\OneDrive";
        foreach (var d in new[] { @"G:\My Drive", @"C:\Users\x\OneDrive - Zeta", @"C:\Users\x\OneDrive - alpha", @"C:\Users\x\OneDrive" })
            p.Existing.Add(d);

        var result = CloudFolderDetector.Detect(p);

        Assert.Equal(
            ["OneDrive", "OneDrive \u2013 alpha", "OneDrive \u2013 Zeta", "Google Drive"],
            result.Select(f => f.DisplayName));
    }

    [Fact]
    public void Detect_NothingInstalled_ReturnsEmpty()
    {
        Assert.Empty(CloudFolderDetector.Detect(new FakeProbe()));
    }

    [Fact]
    public void BackupFolderFor_AppendsSubfolder()
    {
        var f = new CloudFolder(CloudProvider.GoogleDrive, "Google Drive", @"G:\My Drive");

        Assert.Equal(@"G:\My Drive\Prompt Library Backups", CloudFolderDetector.BackupFolderFor(f));
        Assert.Equal("Prompt Library Backups", CloudFolderDetector.BackupSubfolder);
    }
}

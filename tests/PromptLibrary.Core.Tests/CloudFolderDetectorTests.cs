using PromptLibrary.Core.Backup;

namespace PromptLibrary.Core.Tests;

public class CloudFolderDetectorTests
{
    [Fact]
    public void Detect_OneDrivePersonalFromEnv()
    {
        var p = new FakeCloudProbe();
        p.Env["OneDrive"] = @"C:\Users\x\OneDrive";
        p.Existing.Add(@"C:\Users\x\OneDrive");

        var result = CloudFolderDetector.Detect(p);

        Assert.Equal([new CloudFolder(CloudProvider.OneDrive, "OneDrive", @"C:\Users\x\OneDrive")], result);
    }

    [Fact]
    public void Detect_BusinessFromEnvAndRegistry_NameFromFolder()
    {
        var p = new FakeCloudProbe();
        p.Env["OneDriveCommercial"] = @"C:\Users\x\OneDrive - Contoso";
        p.Accounts.Add(("Business2", @"C:\Users\x\OneDrive - Fabrikam", null));
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
        var p = new FakeCloudProbe();
        p.Roots.Add(@"C:\");
        p.Roots.Add(@"G:\");
        p.Existing.Add(@"G:\My Drive");

        var result = CloudFolderDetector.Detect(p);

        Assert.Equal([new CloudFolder(CloudProvider.GoogleDrive, "Google Drive", @"G:\My Drive")], result);
    }

    [Fact]
    public void Detect_GoogleDriveOnOtherLetter()
    {
        var p = new FakeCloudProbe();
        p.Roots.Add(@"H:\");
        p.Existing.Add(@"H:\My Drive");

        var result = CloudFolderDetector.Detect(p);

        Assert.Equal([new CloudFolder(CloudProvider.GoogleDrive, "Google Drive", @"H:\My Drive")], result);
    }

    [Fact]
    public void Detect_GoogleDriveFolderMount()
    {
        var p = new FakeCloudProbe();
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
        var p = new FakeCloudProbe();
        p.Env["OneDrive"] = @"C:\Users\x\OneDrive";
        p.Env["OneDriveCommercial"] = @"C:\Users\x\OneDrive - Contoso";
        p.Accounts.Add(("Business1", @"C:\Users\x\OneDrive - Gone", null));
        p.Roots.Add(@"G:\");

        Assert.Empty(CloudFolderDetector.Detect(p));
    }

    [Fact]
    public void Detect_DeduplicatesEnvAndRegistry()
    {
        var p = new FakeCloudProbe();
        p.Env["OneDriveConsumer"] = @"C:\Users\x\OneDrive";
        p.Env["OneDrive"] = @"c:\users\x\onedrive\";
        p.Accounts.Add(("Personal", @"C:\Users\x\OneDrive\", null));
        p.Env["OneDriveCommercial"] = @"C:\Users\x\OneDrive - Contoso";
        p.Accounts.Add(("Business1", @"c:\users\x\ONEDRIVE - CONTOSO\", null));
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
        var p = new FakeCloudProbe();
        p.Env["OneDrive"] = @"C:\Users\x\OneDrive - Contoso";
        p.Existing.Add(@"C:\Users\x\OneDrive - Contoso");

        var result = CloudFolderDetector.Detect(p);

        Assert.Equal([new CloudFolder(CloudProvider.OneDriveBusiness, "OneDrive \u2013 Contoso", @"C:\Users\x\OneDrive - Contoso")], result);
    }

    [Fact]
    public void Detect_Order()
    {
        var p = new FakeCloudProbe();
        p.Roots.Add(@"G:\");
        p.Env["OneDriveCommercial"] = @"C:\Users\x\OneDrive - Zeta";
        p.Accounts.Add(("Business2", @"C:\Users\x\OneDrive - alpha", null));
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
        Assert.Empty(CloudFolderDetector.Detect(new FakeCloudProbe()));
    }

    [Fact]
    public void BackupFolderFor_AppendsSubfolder()
    {
        var f = new CloudFolder(CloudProvider.GoogleDrive, "Google Drive", @"G:\My Drive");

        Assert.Equal(@"G:\My Drive\Prompt Library Backups", CloudFolderDetector.BackupFolderFor(f));
        Assert.Equal("Prompt Library Backups", CloudFolderDetector.BackupSubfolder);
    }
}

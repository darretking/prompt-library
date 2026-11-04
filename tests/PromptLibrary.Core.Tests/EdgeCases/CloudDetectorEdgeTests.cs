using PromptLibrary.Core.Backup;

namespace PromptLibrary.Core.Tests.EdgeCases;

public class CloudDetectorEdgeTests
{
    private const string LocalAppData = @"C:\Users\x\AppData\Local";
    private const string RoamingAppData = @"C:\Users\x\AppData\Roaming";

    private static CloudFolder OneDrive(string path) => new(CloudProvider.OneDrive, "OneDrive", path);
    private static CloudFolder Business(string org, string path) => new(CloudProvider.OneDriveBusiness, "OneDrive \u2013 " + org, path);
    private static CloudFolder Google(string path) => new(CloudProvider.GoogleDrive, "Google Drive", path);
    private static CloudFolder Dropbox(string path) => new(CloudProvider.Dropbox, "Dropbox", path);
    private static CloudFolder DropboxBusiness(string path) => new(CloudProvider.DropboxBusiness, "Dropbox \u2013 Business", path);
    private static CloudFolder ICloud(string path) => new(CloudProvider.ICloudDrive, "iCloud Drive", path);

    private static FakeCloudProbe ProbeWith(params string[] existing)
    {
        var p = new FakeCloudProbe();
        foreach (var path in existing) p.Existing.Add(path);
        return p;
    }

    private static string DropboxInfo(string? personal, string? business)
    {
        static string Entry(string name, string path) =>
            $"\"{name}\": {{\"path\": \"{path.Replace(@"\", @"\\")}\", \"host\": 123, \"is_team\": false}}";
        var entries = new List<string>();
        if (personal is not null) entries.Add(Entry("personal", personal));
        if (business is not null) entries.Add(Entry("business", business));
        return "{" + string.Join(", ", entries) + "}";
    }

    // ---- General edge cases ----

    [Fact]
    public void Detect_EnvPointsToMissingFolder_Ignored()
    {
        var p = ProbeWith();
        p.Env["OneDriveConsumer"] = @"C:\Users\x\OneDrive";
        p.Env["OneDriveCommercial"] = @"C:\Users\x\OneDrive - Contoso";
        p.Env["LOCALAPPDATA"] = LocalAppData;
        p.Files[LocalAppData + @"\Dropbox\info.json"] = DropboxInfo(@"D:\Dropbox", null);

        Assert.Empty(CloudFolderDetector.Detect(p));
    }

    [Fact]
    public void Detect_TrailingSlashDuplicates_Removed()
    {
        var p = ProbeWith(@"C:\Users\x\OneDrive", @"C:\Users\x\OneDrive\",
            @"C:\Users\x\OneDrive - Contoso", @"C:\Users\x\OneDrive - Contoso\");
        p.Env["OneDriveConsumer"] = @"C:\Users\x\OneDrive\";
        p.Env["OneDriveCommercial"] = @"C:\Users\x\OneDrive - Contoso\";
        p.Accounts.Add(("Personal", @"C:\Users\x\OneDrive", null));
        p.Accounts.Add(("Business1", @"C:\Users\x\OneDrive - Contoso", "Contoso"));

        var result = CloudFolderDetector.Detect(p);

        Assert.Equal([OneDrive(@"C:\Users\x\OneDrive"), Business("Contoso", @"C:\Users\x\OneDrive - Contoso")], result);
    }

    [Fact]
    public void Detect_DriveWithoutMyDrive_Ignored()
    {
        var p = ProbeWith(@"D:\Projects", @"G:\Shared drives");
        p.Roots.AddRange([@"C:\", @"D:\", @"G:\"]);
        p.Labels[@"G:\"] = "Google Drive";

        Assert.Empty(CloudFolderDetector.Detect(p));
    }

    [Fact]
    public void Detect_BothOneDriveAndGoogle()
    {
        var p = ProbeWith(@"C:\Users\x\OneDrive", @"G:\My Drive");
        p.Env["OneDrive"] = @"C:\Users\x\OneDrive";
        p.Roots.Add(@"G:\");

        Assert.Equal([OneDrive(@"C:\Users\x\OneDrive"), Google(@"G:\My Drive")], CloudFolderDetector.Detect(p));
    }

    // ---- OneDrive variants ----

    [Fact]
    public void OneDrivePersonalNamedPersonal_IsPersonal()
    {
        const string folder = @"C:\Users\x\OneDrive - Personal";

        var fromEnv = ProbeWith(folder);
        fromEnv.Env["OneDrive"] = folder;
        var fromRegistry = ProbeWith(folder);
        fromRegistry.Accounts.Add(("Personal", folder, "Personal"));

        Assert.Equal([OneDrive(folder)], CloudFolderDetector.Detect(fromEnv));
        Assert.Equal([OneDrive(folder)], CloudFolderDetector.Detect(fromRegistry));
    }

    [Fact]
    public void OneDriveBusinessOrgFromRegistryDisplayName()
    {
        var p = ProbeWith(@"C:\Users\x\OneDrive - Contoso", @"C:\Users\x\OneDrive - Fabrikam", @"C:\Users\x\Work Files");
        p.Env["OneDriveCommercial"] = @"C:\Users\x\OneDrive - Contoso";
        p.Accounts.Add(("Business1", @"C:\Users\x\OneDrive - Contoso", "Contoso Corporation"));
        p.Accounts.Add(("Business2", @"C:\Users\x\OneDrive - Fabrikam", "  "));
        p.Accounts.Add(("Business3", @"C:\Users\x\Work Files", "Tailspin Toys"));

        var result = CloudFolderDetector.Detect(p);

        Assert.Equal(
        [
            Business("Contoso Corporation", @"C:\Users\x\OneDrive - Contoso"),
            Business("Fabrikam", @"C:\Users\x\OneDrive - Fabrikam"),
            Business("Tailspin Toys", @"C:\Users\x\Work Files"),
        ], result);
    }

    [Fact]
    public void OneDriveConsumerPreferredOverOneDrive()
    {
        var p = ProbeWith(@"C:\Users\x\OneDrive", @"C:\Users\x\OneDrive - Old");
        p.Env["OneDriveConsumer"] = @"C:\Users\x\OneDrive";
        p.Env["OneDrive"] = @"C:\Users\x\OneDrive - Old";

        Assert.Equal([OneDrive(@"C:\Users\x\OneDrive")], CloudFolderDetector.Detect(p));
    }

    // ---- Google Drive ----

    [Theory]
    [InlineData("My Drive")]
    [InlineData("Mon Drive")]
    [InlineData("Meine Ablage")]
    [InlineData("Mi unidad")]
    [InlineData("Mój dysk")]
    [InlineData("Мой диск")]
    [InlineData("マイドライブ")]
    [InlineData("我的云端硬盘")]
    public void GoogleDriveLocalisedMyDrive(string myDrive)
    {
        var onDrive = ProbeWith(@"H:\" + myDrive);
        onDrive.Roots.AddRange([@"C:\", @"H:\"]);
        var folderMount = ProbeWith(@"C:\Users\x\Google Drive\" + myDrive);

        Assert.Equal([Google(@"H:\" + myDrive)], CloudFolderDetector.Detect(onDrive));
        Assert.Equal([Google(@"C:\Users\x\Google Drive\" + myDrive)], CloudFolderDetector.Detect(folderMount));
    }

    [Fact]
    public void GoogleDriveByVolumeLabelOnZ()
    {
        var p = ProbeWith(@"D:\My Drive", @"Z:\Mi unidad");
        p.Roots.AddRange([@"C:\", @"D:\", @"Z:\"]);
        p.Labels[@"Z:\"] = "GOOGLE DRIVE";

        var result = CloudFolderDetector.Detect(p);

        // The drive labelled "Google Drive" comes first.
        Assert.Equal([Google(@"Z:\Mi unidad"), Google(@"D:\My Drive")], result);
    }

    // ---- Dropbox ----

    [Fact]
    public void DropboxFromInfoJson_PersonalAndBusiness()
    {
        var p = ProbeWith(@"D:\Dropbox", @"D:\Dropbox (Contoso)", @"C:\Users\x\Dropbox");
        p.Env["LOCALAPPDATA"] = LocalAppData;
        p.Env["APPDATA"] = RoamingAppData;
        p.Files[LocalAppData + @"\Dropbox\info.json"] = DropboxInfo(@"D:\Dropbox", @"D:\Dropbox (Contoso)");
        p.Files[RoamingAppData + @"\Dropbox\info.json"] = DropboxInfo(@"D:\Dropbox", null);

        var result = CloudFolderDetector.Detect(p);

        // The default folder is only a fallback, so the stale C:\Users\x\Dropbox is not offered.
        Assert.Equal([Dropbox(@"D:\Dropbox"), DropboxBusiness(@"D:\Dropbox (Contoso)")], result);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"personal\": {\"path\": 42}}")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{\"personal\": \"C:\\\\Dropbox\"}")]
    [InlineData("")]
    public void DropboxMalformedInfoJson_Ignored(string json)
    {
        var p = ProbeWith(@"C:\Users\x\Dropbox", @"C:\Users\x\OneDrive");
        p.Env["OneDrive"] = @"C:\Users\x\OneDrive";
        p.Env["LOCALAPPDATA"] = LocalAppData;
        p.Files[LocalAppData + @"\Dropbox\info.json"] = json;

        var result = CloudFolderDetector.Detect(p);

        Assert.Equal([OneDrive(@"C:\Users\x\OneDrive"), Dropbox(@"C:\Users\x\Dropbox")], result);
    }

    [Fact]
    public void DropboxDefaultFolderFallback()
    {
        var p = ProbeWith(@"C:\Users\x\Dropbox");
        p.Env["LOCALAPPDATA"] = LocalAppData; // no info.json there

        Assert.Equal([Dropbox(@"C:\Users\x\Dropbox")], CloudFolderDetector.Detect(p));
    }

    // ---- iCloud Drive ----

    [Fact]
    public void ICloudDrive()
    {
        var p = ProbeWith(@"C:\Users\x\iCloudDrive");

        Assert.Equal([ICloud(@"C:\Users\x\iCloudDrive")], CloudFolderDetector.Detect(p));
    }

    // ---- Robustness ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyUserProfile_NoRelativePaths(string userProfile)
    {
        var p = ProbeWith("Dropbox", "iCloudDrive", "My Drive", @"Google Drive\My Drive", "OneDrive",
            userProfile + @"\Dropbox", userProfile + @"\iCloudDrive", @"Dropbox\info.json");
        p.UserProfile = userProfile;
        p.Env["OneDriveConsumer"] = "  ";
        p.Env["OneDrive"] = "OneDrive";
        p.Env["OneDriveCommercial"] = "";
        p.Env["LOCALAPPDATA"] = " ";
        p.Env["APPDATA"] = "";
        p.Accounts.Add(("Personal", "OneDrive", null));
        p.Accounts.Add(("Business1", " ", "Contoso"));

        Assert.Empty(CloudFolderDetector.Detect(p));
        Assert.All(p.Queried, path => Assert.True(Path.IsPathFullyQualified(path), $"Relative path probed: '{path}'"));
    }

    [Fact]
    public void ProbeThrows_DetectionStillReturnsOthers()
    {
        var p = ProbeWith(@"C:\Users\x\OneDrive", @"C:\Users\x\iCloudDrive", @"G:\My Drive");
        p.Env["OneDrive"] = @"C:\Users\x\OneDrive";
        p.Env["LOCALAPPDATA"] = LocalAppData;
        p.Roots.Add(@"G:\");
        p.ThrowFromLookups = new IOException("device not ready");

        Assert.Equal([OneDrive(@"C:\Users\x\OneDrive"), ICloud(@"C:\Users\x\iCloudDrive")], CloudFolderDetector.Detect(p));

        p.ThrowFromEverything = new UnauthorizedAccessException("denied");
        Assert.Empty(CloudFolderDetector.Detect(p));
    }

    [Fact]
    public void NonAsciiUserProfile()
    {
        const string home = @"C:\Users\José María";
        var p = ProbeWith(home + @"\OneDrive", home + @"\Google Drive\Meine Ablage", home + @"\Dropbox", home + @"\iCloudDrive");
        p.UserProfile = home;
        p.Env["OneDrive"] = home + @"\OneDrive";

        Assert.Equal(
        [
            OneDrive(home + @"\OneDrive"),
            Google(home + @"\Google Drive\Meine Ablage"),
            Dropbox(home + @"\Dropbox"),
            ICloud(home + @"\iCloudDrive"),
        ], CloudFolderDetector.Detect(p));
    }

    [Fact]
    public void AllProvidersOrder()
    {
        var p = ProbeWith(@"C:\Users\x\iCloudDrive", @"C:\Users\x\Dropbox (Contoso)", @"C:\Users\x\Dropbox",
            @"G:\My Drive", @"C:\Users\x\OneDrive - Zeta", @"C:\Users\x\OneDrive - alpha", @"C:\Users\x\OneDrive");
        p.Roots.Add(@"G:\");
        p.Env["LOCALAPPDATA"] = LocalAppData;
        p.Files[LocalAppData + @"\Dropbox\info.json"] = DropboxInfo(@"C:\Users\x\Dropbox", @"C:\Users\x\Dropbox (Contoso)");
        p.Accounts.Add(("Business2", @"C:\Users\x\OneDrive - Zeta", "Zeta"));
        p.Accounts.Add(("Business1", @"C:\Users\x\OneDrive - alpha", "alpha"));
        p.Accounts.Add(("Personal", @"C:\Users\x\OneDrive", null));

        var result = CloudFolderDetector.Detect(p);

        Assert.Equal(
            ["OneDrive", "OneDrive \u2013 alpha", "OneDrive \u2013 Zeta", "Google Drive", "Dropbox", "Dropbox \u2013 Business", "iCloud Drive"],
            result.Select(f => f.DisplayName));
        Assert.Equal(
            [CloudProvider.OneDrive, CloudProvider.OneDriveBusiness, CloudProvider.OneDriveBusiness, CloudProvider.GoogleDrive,
             CloudProvider.Dropbox, CloudProvider.DropboxBusiness, CloudProvider.ICloudDrive],
            result.Select(f => f.Provider));
    }
}

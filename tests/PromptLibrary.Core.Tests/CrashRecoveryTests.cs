using PromptLibrary.Core.Editing;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.Core.Tests;

/// <summary>The crash log and the recovery copy of a library that couldn't be saved.</summary>
public sealed class CrashRecoveryTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 5, 9, 4, 7);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PromptLibraryTests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static Library SampleLibrary()
    {
        var categoryId = Guid.NewGuid();
        return new Library
        {
            Categories = [new Category { Id = categoryId, Name = "Coding" }],
            Prompts = [new Prompt { Id = Guid.NewGuid(), Title = "Review", Text = "Check this.", CategoryId = categoryId }],
        };
    }

    [Fact]
    public void CrashLog_Append_CreatesTheFolderAndAddsTimestampedEntries()
    {
        var log = Path.Combine(_dir, "sub", "crash.log");

        CrashLog.Append(log, new InvalidOperationException("first boom"), Now);
        CrashLog.Append(log, new IOException("second boom"), Now.AddMinutes(1));

        var text = File.ReadAllText(log);
        Assert.Contains("2026-11-05 09:04:07", text);
        Assert.Contains("System.InvalidOperationException: first boom", text);
        Assert.Contains("2026-11-05 09:05:07", text);
        Assert.Contains("System.IO.IOException: second boom", text);
        Assert.True(text.IndexOf("first boom", StringComparison.Ordinal) < text.IndexOf("second boom", StringComparison.Ordinal));
    }

    [Fact]
    public void CrashLog_Append_NeverThrows()
    {
        Directory.CreateDirectory(_dir);
        // The log path is a folder, so it can't be written.
        CrashLog.Append(_dir, new InvalidOperationException("boom"), Now);
    }

    [Fact]
    public void LibraryRecoveryFileName_UsesLocalTimestamp()
        => Assert.Equal("library-2026-11-05_090407.json", RecoveredLibrary.FileName(Now));

    [Fact]
    public void RecoveredLibrary_Write_SavesALoadableCopy_NeverOverwriting()
    {
        var library = SampleLibrary();

        var first = RecoveredLibrary.Write(_dir, library, Now);
        var second = RecoveredLibrary.Write(_dir, library, Now);

        Assert.Equal(Path.Combine(_dir, "library-2026-11-05_090407.json"), first);
        Assert.Equal(Path.Combine(_dir, "library-2026-11-05_090407-2.json"), second);
        var loaded = JsonStore.LoadLibrary(first);
        Assert.Equal(LoadStatus.Ok, loaded.Status);
        Assert.Equal("Review", loaded.Value!.Prompts[0].Title);
        Assert.False(File.Exists(first + ".bak"));
    }
}

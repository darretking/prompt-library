# Prompt Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows tray app that stores AI prompts (title, description, category, text) and copies any prompt to the clipboard from a quick menu opened by a global shortcut or the tray icon, with automatic backups into a Google Drive for desktop folder.

**Architecture:** Two projects. `PromptLibrary.Core` (plain `net10.0` class library, no UI) holds the models, JSON storage, editing rules, search, quick-menu shaping, hotkey parsing and backup/restore — all unit-tested. `PromptLibrary.App` (`net10.0-windows`, WPF + WinForms) is a thin shell: tray icon and native menu (WinForms `NotifyIcon` + `ContextMenuStrip`), global hotkey (`RegisterHotKey`), clipboard, startup registration, and the WPF windows (three-column main window, settings, restore). App logic stays in Core so the owner (a C# developer) can read and change it easily.

**Tech Stack:** C# 13, .NET 10 (SDK 10.0.401 is installed), WPF + WinForms interop, `System.Text.Json`, CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-04-prompt-library-design.md` — read it before starting any task. Appendix A holds the 12 seed prompts verbatim.

## Global Constraints

- Target `net10.0` (Core, Tests) and `net10.0-windows` (App). `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`.
- Data folder: `%APPDATA%\PromptLibrary\` containing `library.json` (categories + prompts, backed up) and `settings.json` (machine settings, never backed up or restored).
- All file writes atomic: write `<file>.tmp`, then `File.Move(tmp, file, overwrite: true)`.
- `library.json` has `SchemaVersion` = `1`.
- Prompt text normalised to CRLF on save and on copy; every other character kept exactly.
- If `library.json` exists but can't be read, never overwrite it.
- Backup file name: `PromptLibrary-backup-YYYY-MM-DD_HHmmss.json` (local time). Pruning only touches files matching exactly that pattern.
- Defaults: hotkey `Ctrl+Alt+P`; backups to keep `30`; start with Windows `true`; suggested backup folder `G:\My Drive\Prompt Library Backups` only if `G:\My Drive` exists.
- Never use `Ctrl+Shift+P` as a default.
- Startup registration: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value name `PromptLibrary`, value = quoted path of the running exe (rewritten on every start so it stays current).
- Single running copy; a second launch opens the first copy's main window.
- Quick menu: categories and prompts sorted A–Z (case-insensitive); empty categories hidden; trailing items `Open Prompt Library…` and `Exit`.
- Copy confirmation text: `Copied: <title>`.
- First-run tray message: `Prompt Library is running. Press Ctrl+Alt+P or click this icon.` (use the actual configured hotkey text).
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **`&` in a category or prompt title** (e.g. "Q&A") — WinForms menus treat `&` as a keyboard-accelerator marker; the menu must show the literal `Q&A`. Pinned in Task 4 (`MenuText.Escape`).
2. **Very long prompt titles** — the quick menu must stay usable; titles longer than 60 characters are cut to 59 + `…` in the menu (full title stays in the main window). Pinned in Task 4.
3. **Clipboard locked by another app** (password managers, RDP) — retry 5 times, 50 ms apart, then show `Couldn't copy — clipboard busy. Try again.`; never crash. Pinned in Task 7 (`ClipboardWriter` with an injectable setter, tested with a fake); Task 8 shows the message.
4. **Backup folder unavailable** (Drive for desktop not running, `G:` missing) — backup returns a failure result, the app shows a tray message and carries on; `LastBackupAt` is not updated so the next trigger retries. Pinned in Task 6.
5. **Restoring a corrupt or non-library backup file** — rejected before anything is touched; current library unchanged and no pre-restore backup consumed. Pinned in Task 6.

---

## File Structure

```
PromptLibrary.sln
src/PromptLibrary.Core/
  PromptLibrary.Core.csproj
  Models/Category.cs            Category record-like class
  Models/Prompt.cs              Prompt class
  Models/Library.cs             Library root (SchemaVersion, Categories, Prompts)
  Models/AppSettings.cs         settings.json shape + defaults
  Text/LineEndings.cs           ToCrlf
  Storage/AtomicFile.cs         atomic write
  Storage/JsonStore.cs          load/save library.json and settings.json
  Editing/LibraryEditor.cs      category/prompt add/rename/update/delete rules
  Search/PromptFilter.cs        category + text filter, ordering for middle column
  Menu/QuickMenuBuilder.cs      shapes Library into menu model
  Menu/MenuText.cs              & escaping + title truncation
  Hotkeys/HotkeyGesture.cs      parse/format "Ctrl+Alt+P"
  Backup/BackupService.cs       backup, should-backup, prune, list, restore
  Backup/LibraryHash.cs         SHA-256 of library.json
  Seed/SeedLibrary.cs           12 seed prompts from spec Appendix A
  Clipboard/ClipboardWriter.cs  retry wrapper around an injected setter
tests/PromptLibrary.Core.Tests/
  PromptLibrary.Core.Tests.csproj
  LineEndingsTests.cs, JsonStoreTests.cs, LibraryEditorTests.cs,
  PromptFilterTests.cs, QuickMenuBuilderTests.cs, MenuTextTests.cs,
  HotkeyGestureTests.cs, BackupServiceTests.cs, SeedLibraryTests.cs,
  ClipboardWriterTests.cs
src/PromptLibrary.App/
  PromptLibrary.App.csproj
  App.xaml / App.xaml.cs        single instance, startup, shutdown
  AppHost.cs                    owns in-memory Library + settings, save, backup scheduling
  Paths.cs                      %APPDATA% locations
  Tray/TrayIcon.cs              NotifyIcon, quick menu, balloons
  Hotkeys/GlobalHotkey.cs       RegisterHotKey via hidden HwndSource
  Startup/StartupRegistration.cs HKCU Run key
  ViewModels/MainViewModel.cs, ViewModels/PromptEditorViewModel.cs
  Views/MainWindow.xaml(.cs), Views/SettingsWindow.xaml(.cs),
  Views/RestoreWindow.xaml(.cs), Views/MoveCategoryDialog.xaml(.cs)
  Assets/app.ico
docs/manual-test-checklist.md
```

---

### Task 1: Solution, models, line endings

**Files:**
- Create: `PromptLibrary.sln`, `src/PromptLibrary.Core/PromptLibrary.Core.csproj`, `src/PromptLibrary.Core/Models/{Category,Prompt,Library,AppSettings}.cs`, `src/PromptLibrary.Core/Text/LineEndings.cs`, `tests/PromptLibrary.Core.Tests/PromptLibrary.Core.Tests.csproj`
- Test: `tests/PromptLibrary.Core.Tests/LineEndingsTests.cs`

**Interfaces:**
- Produces:
  - `class Category { Guid Id; string Name; }`
  - `class Prompt { Guid Id; string Title; string Description = ""; Guid CategoryId; string Text; DateTimeOffset CreatedAt; DateTimeOffset UpdatedAt; }`
  - `class Library { int SchemaVersion = 1; List<Category> Categories; List<Prompt> Prompts; }` (all public get/set properties, JSON camelCase)
  - `class AppSettings { string Hotkey = "Ctrl+Alt+P"; string? BackupFolder; bool StartWithWindows = true; int BackupsToKeep = 30; DateTimeOffset? LastBackupAt; string? LastBackedUpLibraryHash; bool FirstRunDone; }`
  - `static class LineEndings { static string ToCrlf(string text); }`

- [ ] **Step 1: Scaffold.** `dotnet new sln -n PromptLibrary`; `dotnet new classlib -o src/PromptLibrary.Core -f net10.0`; `dotnet new xunit -o tests/PromptLibrary.Core.Tests -f net10.0`; add both to sln; test project references Core. Delete template `Class1.cs`/`UnitTest1.cs`.

- [ ] **Step 2: Write failing tests** in `LineEndingsTests.cs`:

```csharp
[Theory]
[InlineData("a\nb", "a\r\nb")]
[InlineData("a\r\nb", "a\r\nb")]
[InlineData("a\rb", "a\r\nb")]
[InlineData("a\n\n\nb", "a\r\n\r\n\r\nb")]
[InlineData("  tab\there  \n", "  tab\there  \r\n")]
[InlineData("", "")]
public void ToCrlf_NormalisesAllLineEndings_KeepsEverythingElse(string input, string expected)
    => Assert.Equal(expected, LineEndings.ToCrlf(input));

[Fact]
public void ToCrlf_KeepsEmojiQuotesBraces()
    => Assert.Equal("\"{x}\" 🙂\r\n'y'", LineEndings.ToCrlf("\"{x}\" 🙂\n'y'"));
```

- [ ] **Step 3: Run** `dotnet test` → FAIL (`LineEndings` not defined).
- [ ] **Step 4: Implement** models and `LineEndings.ToCrlf` (normalise `\r\n` and lone `\r` to `\n` first, then `\n` → `\r\n`).
- [ ] **Step 5: Run** `dotnet test` → PASS.
- [ ] **Step 6: Commit** `feat: solution scaffold, models, CRLF normalisation`.

---

### Task 2: Atomic JSON storage

**Files:**
- Create: `src/PromptLibrary.Core/Storage/AtomicFile.cs`, `src/PromptLibrary.Core/Storage/JsonStore.cs`
- Test: `tests/PromptLibrary.Core.Tests/JsonStoreTests.cs` (each test uses its own temp directory)

**Interfaces:**
- Consumes: Task 1 models, `LineEndings.ToCrlf`.
- Produces:
  - `static class AtomicFile { static void WriteAllText(string path, string contents); }` — UTF-8 without BOM.
  - `enum LoadStatus { Ok, Missing, Unreadable }`
  - `record LoadResult<T>(LoadStatus Status, T? Value, string? Error);`
  - `static class JsonStore {`
    `static LoadResult<Library> LoadLibrary(string path);`
    `static void SaveLibrary(string path, Library library);` — normalises every `Prompt.Text` and `Description` with `ToCrlf` before writing; indented JSON, camelCase, `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` so emoji/quotes stay readable.
    `static LoadResult<AppSettings> LoadSettings(string path);`
    `static void SaveSettings(string path, AppSettings settings);`
    `static Library ParseLibrary(string json);` — throws `InvalidDataException` if JSON invalid, `schemaVersion` missing/≠1, or any prompt references a missing category. }`

- [ ] **Step 1: Write failing tests:**
  - `SaveThenLoad_RoundTripsTrickyText` — prompt text `"Line1\n\n```bash\ndotnet run\n```\n{x} \"q\" 'a' 🙂\t end  "`; after save+load, `Text == LineEndings.ToCrlf(original)`; status `Ok`.
  - `Load_MissingFile_ReturnsMissing`.
  - `Load_InvalidJson_ReturnsUnreadable_AndFileUntouched` — write `"{ not json"`, load, assert `Unreadable`, file bytes unchanged.
  - `Load_WrongSchemaVersion_ReturnsUnreadable` — `schemaVersion: 2`.
  - `Load_PromptWithUnknownCategory_ReturnsUnreadable`.
  - `Save_LeavesNoTmpFile` — after save, `path + ".tmp"` does not exist.
  - `Settings_RoundTrip_AndMissingGivesDefaults` — missing → `Missing`; save then load keeps `Hotkey`, `BackupsToKeep`.
  - `SavedJson_IsHumanReadable` — file text contains `"🙂"` literally and a newline (indented).
- [ ] **Step 2: Run** `dotnet test --filter JsonStoreTests` → FAIL.
- [ ] **Step 3: Implement** `AtomicFile` and `JsonStore` per signatures above.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat: atomic JSON storage for library and settings`.

---

### Task 3: Editing rules

**Files:**
- Create: `src/PromptLibrary.Core/Editing/LibraryEditor.cs`
- Test: `tests/PromptLibrary.Core.Tests/LibraryEditorTests.cs`

**Interfaces:**
- Consumes: models.
- Produces: `class LibraryEditor(Library library, Func<DateTimeOffset> now)` with
  - `Category AddCategory(string name)`
  - `void RenameCategory(Guid id, string newName)`
  - `void DeleteCategory(Guid id, Guid? movePromptsTo)` — throws `InvalidOperationException` if the category has prompts and `movePromptsTo` is null or equals `id` or doesn't exist.
  - `int PromptCount(Guid categoryId)`
  - `Prompt AddPrompt(Guid categoryId, string title, string description, string text)`
  - `void UpdatePrompt(Guid id, string title, string description, Guid categoryId, string text)` — sets `UpdatedAt = now()`.
  - `void DeletePrompt(Guid id)`
  - Validation throws `ArgumentException`: category name empty/whitespace or duplicate (case-insensitive, trimmed); prompt title empty/whitespace; prompt text empty; unknown category id. Names and titles stored trimmed.

- [ ] **Step 1: Write failing tests:** `AddCategory_DuplicateNameDifferentCase_Throws`, `AddCategory_Blank_Throws`, `RenameCategory_ToOwnNameDifferentCase_Allowed`, `DeleteCategory_Empty_Removes`, `DeleteCategory_WithPrompts_NoTarget_Throws_AndNothingChanges`, `DeleteCategory_WithPrompts_MovesThenRemoves` (prompts' `CategoryId` == target, prompt count unchanged), `AddPrompt_BlankTitle_Throws`, `AddPrompt_EmptyDescription_Allowed`, `UpdatePrompt_SetsUpdatedAt` (fixed clock), `UpdatePrompt_UnknownCategory_Throws`.
- [ ] **Step 2: Run** `dotnet test --filter LibraryEditorTests` → FAIL.
- [ ] **Step 3: Implement** `LibraryEditor`.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat: library editing rules`.

---

### Task 4: Search, quick-menu shaping, menu text

**Files:**
- Create: `src/PromptLibrary.Core/Search/PromptFilter.cs`, `src/PromptLibrary.Core/Menu/QuickMenuBuilder.cs`, `src/PromptLibrary.Core/Menu/MenuText.cs`
- Test: `PromptFilterTests.cs`, `QuickMenuBuilderTests.cs`, `MenuTextTests.cs`

**Interfaces:**
- Consumes: models.
- Produces:
  - `static class PromptFilter { static IReadOnlyList<Prompt> Apply(Library lib, Guid? categoryId, string? query); }` — `categoryId` null = all; query matches title, description or text, case-insensitive, trimmed; blank query = no text filter; result sorted by title A–Z (`StringComparer.CurrentCultureIgnoreCase`).
  - `record QuickMenuPrompt(Guid Id, string Title);`
  - `record QuickMenuCategory(string Name, IReadOnlyList<QuickMenuPrompt> Prompts);`
  - `static class QuickMenuBuilder { static IReadOnlyList<QuickMenuCategory> Build(Library lib); }` — A–Z categories and prompts, empty categories omitted.
  - `static class MenuText { const int MaxLength = 60; static string Escape(string s); static string Truncate(string s); static string ForMenu(string s) => Escape(Truncate(s)); }` — `Escape` doubles `&`; `Truncate` returns `s` if ≤60 chars else first 59 + `…`.

- [ ] **Step 1: Write failing tests:**
  - `Filter_AllCategories_BlankQuery_ReturnsAllSorted`.
  - `Filter_ByCategory_OnlyThatCategory`.
  - `Filter_QueryMatchesTextOnly_CaseInsensitive` (query `"XUNIT"` finds a prompt whose text contains "xUnit" but title doesn't).
  - `Build_SortsCategoriesAndPrompts_CaseInsensitive` (`"beta","Alpha","gamma"` → `Alpha, beta, gamma`).
  - `Build_OmitsEmptyCategories`.
  - `Build_EmptyLibrary_ReturnsEmpty`.
  - `Escape_DoublesAmpersand` (`"Q&A"` → `"Q&&A"`).
  - `Truncate_61Chars_Returns59PlusEllipsis` (length 60, ends with `…`); `Truncate_60Chars_Unchanged`.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** the three classes.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat: prompt search and quick menu model`.

---

### Task 5: Seed library and hotkey parsing

**Files:**
- Create: `src/PromptLibrary.Core/Seed/SeedLibrary.cs`, `src/PromptLibrary.Core/Hotkeys/HotkeyGesture.cs`
- Test: `SeedLibraryTests.cs`, `HotkeyGestureTests.cs`

**Interfaces:**
- Produces:
  - `static class SeedLibrary { static Library Create(DateTimeOffset now); }` — the 12 prompts and 6 categories from spec **Appendix A**, titles/descriptions/text copied verbatim (prompt 3 description is `""`). Use C# raw string literals (`"""..."""`, `""""...""""` for prompt 10 which contains ```` ``` ````).
  - `[Flags] enum HotkeyModifiers { None=0, Alt=1, Ctrl=2, Shift=4, Win=8 }` (values match Win32 `MOD_*`).
  - `record HotkeyGesture(HotkeyModifiers Modifiers, string Key)` with `static bool TryParse(string text, out HotkeyGesture? gesture)` and `override string ToString()` → canonical order `Ctrl+Alt+Shift+Win+Key`. Key is a single letter A–Z, digit 0–9, or `F1`–`F24` (uppercased). At least one modifier required; `Shift`-only is rejected (it would steal typing).

- [ ] **Step 1: Write failing tests:**
  - `Seed_Has12PromptsIn6Categories`; category names exactly `Coding, Testing, User Interface, Marketing, Documentation, Code Review`.
  - `Seed_FixThisError_HasEmptyDescription`.
  - `Seed_WriteReadme_ContainsBashFence` (text contains ```` ```bash ````).
  - `Seed_EmptyStateCopy_ContainsEmoji` (`🙂`).
  - `Seed_RoundTripsThroughJsonStore` (save/load equals after `ToCrlf`).
  - `TryParse_CtrlAltP_Ok`; `TryParse_IsCaseAndOrderInsensitive` (`"alt+ctrl+p"` → `ToString()=="Ctrl+Alt+P"`); `TryParse_NoModifier_Fails`; `TryParse_ShiftOnly_Fails`; `TryParse_F12WithWin_Ok`; `TryParse_Garbage_Fails` (`"Ctrl+"`, `"Ctrl+Alt+PP"`, `""`).
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat: seed prompts and hotkey parsing`.

---

### Task 6: Backup and restore

**Files:**
- Create: `src/PromptLibrary.Core/Backup/BackupService.cs`, `src/PromptLibrary.Core/Backup/LibraryHash.cs`
- Test: `BackupServiceTests.cs` (temp dirs, fixed clock)

**Interfaces:**
- Consumes: `JsonStore.ParseLibrary`, `AtomicFile`.
- Produces:
  - `static class LibraryHash { static string OfFile(string path); }` — lowercase hex SHA-256 of file bytes.
  - `enum BackupTrigger { Daily, OnExit, Manual }`
  - `record BackupResult(bool Success, string? BackupPath, string? Hash, string? Error);`
  - `record BackupInfo(string Path, DateTime Timestamp, int PromptCount);` (`PromptCount = -1` if file unreadable)
  - `class BackupService(Func<DateTime> nowLocal)` with:
    - `const string FilePrefix = "PromptLibrary-backup-";` and name format `PromptLibrary-backup-yyyy-MM-dd_HHmmss.json`
    - `bool ShouldBackup(BackupTrigger trigger, string currentHash, AppSettings s)` — `Manual` → true; otherwise false if `currentHash == s.LastBackedUpLibraryHash`; `Daily` additionally requires `s.LastBackupAt` null or ≥ 24 h ago; `OnExit` → true when changed.
    - `BackupResult BackupNow(string libraryPath, string backupFolder, int keep)` — creates folder if parent exists, copies file, prunes. On any IO error returns `Success=false` with message; never throws. If a file with the same second-resolution name exists, append `-1`, `-2`.
    - `void Prune(string backupFolder, int keep)` — deletes oldest beyond `keep` among files matching `^PromptLibrary-backup-\d{4}-\d{2}-\d{2}_\d{6}(-\d+)?\.json$` only.
    - `IReadOnlyList<BackupInfo> List(string backupFolder)` — newest first, timestamp parsed from file name.
    - `BackupResult Restore(string backupPath, string libraryPath, string backupFolder, int keep)` — parse backup with `ParseLibrary` first (fail → `Success=false`, nothing touched); then `BackupNow` current library (fail → abort, nothing touched); then atomic-replace `libraryPath` with backup content.

- [ ] **Step 1: Write failing tests:**
  - `BackupNow_CreatesNamedFile` (clock 2026-11-04 09:05:07 → `PromptLibrary-backup-2026-11-04_090507.json`, content identical).
  - `BackupNow_SameSecondTwice_AppendsSuffix`.
  - `BackupNow_FolderUnavailable_ReturnsFailure_DoesNotThrow` (backup folder = `Path.Combine(existingFile, "sub")`, i.e. its parent is a file, so it can't be created; assert `Success == false`, `Error` non-empty).
  - `Prune_KeepsNewest30_IgnoresOtherFiles` (35 backups + `notes.txt` + `PromptLibrary-backup-old.json` → 30 backups remain, both other files untouched).
  - `ShouldBackup_Unchanged_FalseExceptManual`; `ShouldBackup_Daily_ChangedButWithin24h_False`; `ShouldBackup_Daily_ChangedAfter24h_True`; `ShouldBackup_OnExit_Changed_True`.
  - `List_NewestFirst_WithPromptCounts`.
  - `Restore_ReplacesLibrary_AndBacksUpCurrentFirst` (backup count +1, library content == chosen backup).
  - `Restore_CorruptBackup_Fails_NothingTouched` (library bytes unchanged, backup count unchanged).
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat: backup, prune and restore`.

---

### Task 7: Clipboard retry wrapper

**Files:**
- Create: `src/PromptLibrary.Core/Clipboard/ClipboardWriter.cs`
- Test: `ClipboardWriterTests.cs`

**Interfaces:**
- Produces: `class ClipboardWriter(Action<string> setText, Action<TimeSpan> sleep, int attempts = 5, int delayMs = 50)` with `bool TryCopy(string text)` — calls `setText(LineEndings.ToCrlf(text))`; on `Exception` sleeps `delayMs` and retries up to `attempts` total; returns false after last failure.

- [ ] **Step 1: Write failing tests:** `TryCopy_SucceedsFirstTry_PassesCrlfText`; `TryCopy_FailsTwiceThenSucceeds_ReturnsTrue_SleptTwice`; `TryCopy_AlwaysFails_ReturnsFalse_After5Attempts`.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat: clipboard retry wrapper`.

---

### Task 8: App shell — tray, quick menu, hotkey, single instance, startup

**Files:**
- Create: `src/PromptLibrary.App/PromptLibrary.App.csproj` (`net10.0-windows`, `<UseWPF>true</UseWPF>`, `<UseWindowsForms>true</UseWindowsForms>`, `<OutputType>WinExe</OutputType>`, `<ApplicationIcon>Assets\app.ico</ApplicationIcon>`, package `CommunityToolkit.Mvvm`), `App.xaml(.cs)`, `AppHost.cs`, `Paths.cs`, `Tray/TrayIcon.cs`, `Hotkeys/GlobalHotkey.cs`, `Startup/StartupRegistration.cs`, `Assets/app.ico`
- Modify: `PromptLibrary.sln` (add App project)

**Interfaces:**
- Consumes: `JsonStore`, `SeedLibrary`, `QuickMenuBuilder`, `MenuText.ForMenu`, `HotkeyGesture`, `ClipboardWriter`, `LibraryHash`, `BackupService`.
- Produces (used by Tasks 9–10):
  - `static class Paths { static string DataFolder; static string LibraryFile; static string SettingsFile; }`
  - `class AppHost` — `Library Library { get; }`, `AppSettings Settings { get; }`, `event EventHandler? LibraryChanged`, `void SaveLibrary()` (save + raise `LibraryChanged` + rebuild tray menu), `void SaveSettings()`, `bool CopyPrompt(Guid promptId)` (copies + shows `Copied: <title>` or failure balloon), `void ShowMainWindow()`, `void OpenSettings()`, `void ExitApp()`, `bool ApplyHotkey(string text)`, `TrayIcon Tray { get; }`.
  - `class TrayIcon : IDisposable` — `void Rebuild(IReadOnlyList<QuickMenuCategory>)`, `void ShowMenuAtCursor()`, `void ShowMessage(string title, string text, bool isError = false)`, events `PromptClicked(Guid)`, `OpenRequested`, `ExitRequested`.
  - `class GlobalHotkey : IDisposable` — `bool Register(HotkeyGesture g)`, `void Unregister()`, `event EventHandler? Pressed`.
  - `static class StartupRegistration { static void Apply(bool enabled, string exePath); }`

Behaviour to implement:
- **Single instance:** named `Mutex` `Local\PromptLibrary.SingleInstance`; second instance signals a named `EventWaitHandle` `Local\PromptLibrary.Show` and exits; first instance listens on a background thread and calls `ShowMainWindow()` via the dispatcher.
- **Startup:** `ShutdownMode = OnExplicitShutdown`. Load settings (Missing → defaults). Load library: `Ok` → use; `Missing` → `SeedLibrary.Create` and save; `Unreadable` → do **not** overwrite; show a message box with the error and offer "Restore from backup…" (opens RestoreWindow from Task 10 — until Task 10 lands, just show the error and exit).
- **Tray:** WinForms `NotifyIcon` + `ContextMenuStrip` built from `QuickMenuBuilder.Build`, texts through `MenuText.ForMenu`. Empty library → disabled `No prompts yet`. Then separator, `Open Prompt Library…`, `Exit`. Left- or right-click → `ShowMenuAtCursor()`. Before showing, call `SetForegroundWindow` on the menu's owner handle so the menu closes when clicking elsewhere. Double-click → `ShowMainWindow()`.
- **Hotkey:** hidden `HwndSource` (message-only window, `HWND_MESSAGE` parent), `RegisterHotKey(hwnd, 1, mods | MOD_NOREPEAT, vk)`, handle `WM_HOTKEY` → `ShowMenuAtCursor()`. Registration failure → tray message `Shortcut <text> is in use by another app. Pick another in Settings.` and open settings (Task 10; until then just the message).
- **Copy:** `ClipboardWriter(t => System.Windows.Clipboard.SetText(t, TextDataFormat.UnicodeText), Thread.Sleep)`; success → balloon `Copied: <title>`; failure → `Couldn't copy — clipboard busy. Try again.`
- **Startup registration:** on every launch `StartupRegistration.Apply(settings.StartWithWindows, Environment.ProcessPath!)`.
- **First run** (`!settings.FirstRunDone`): show tray message `Prompt Library is running. Press <hotkey> or click this icon.`, set `FirstRunDone = true`, save settings.

- [ ] **Step 1: Implement** the files above. Use a placeholder `MainWindow` with just a title so `ShowMainWindow()` works (fleshed out in Task 9).
- [ ] **Step 2: Build** `dotnet build` → 0 errors, 0 warnings in App.
- [ ] **Step 3: Manual verify** (`dotnet run --project src/PromptLibrary.App`):
  - tray icon appears; first-run message shows;
  - `%APPDATA%\PromptLibrary\library.json` exists with 12 seed prompts;
  - Ctrl+Alt+P in Notepad opens the menu at the mouse; Coding → Explain this code; Ctrl+V in Notepad pastes exact text; `Copied: Explain this code` shows;
  - clicking elsewhere closes the menu;
  - launching a second copy opens the first's window and the second exits;
  - `HKCU\...\Run\PromptLibrary` exists with the exe path.
- [ ] **Step 4: Run** `dotnet test` → all Core tests still PASS.
- [ ] **Step 5: Commit** `feat: tray app shell with quick menu, hotkey, single instance`.

---

### Task 9: Main window (three columns)

**Files:**
- Create: `ViewModels/MainViewModel.cs`, `ViewModels/PromptEditorViewModel.cs`, `Views/MoveCategoryDialog.xaml(.cs)`
- Modify: `Views/MainWindow.xaml(.cs)` (replace placeholder), `AppHost.cs` (`ShowMainWindow` reuses one window; closing hides it)

**Interfaces:**
- Consumes: `AppHost`, `LibraryEditor`, `PromptFilter`.
- Produces: `MainViewModel` with `ObservableCollection<CategoryItem> Categories` (first item "All prompts", `Id = null`, each with `Count`), `CategoryItem? SelectedCategory`, `string SearchText`, `ObservableCollection<Prompt> Prompts`, `PromptEditorViewModel? Editor`, commands `NewCategory`, `RenameCategory`, `DeleteCategory`, `NewPrompt`, `Save`, `Copy`, `Delete`, `BackupNow`, `Restore`, `OpenSettings`, `Exit`; `bool ConfirmDiscardOrSave()` used before switching prompt/category and on window close.

Layout per mockup option A (spec §6.4): left 180 px categories with counts + `+ New category`; middle 240 px search box + prompt list (title bold, description below, italic `No description` when empty) + `+ New prompt`; right editor: Title, Description, Category `ComboBox` (existing categories only), Prompt `TextBox` (`AcceptsReturn`, `AcceptsTab`, `TextWrapping=Wrap`, `FontFamily="Cascadia Mono, Consolas"`, vertical scroll), buttons Delete / Copy / Save. Menu bar: File → Back up now, Restore from backup…, Settings…, Exit (Restore/Settings wired in Task 10).

Behaviour:
- Category right-click → Rename (inline or small input dialog) / Delete. Delete with prompts → `MoveCategoryDialog` listing other categories (Cancel aborts). Validation errors from `LibraryEditor` shown as message box text.
- `+ New prompt` → creates in selected category (first category A–Z if "All prompts"; if no categories exist, ask to create one first), selects it, focuses Title.
- Unsaved changes on switching prompt, switching category or closing window → `Save changes?` Yes/No/Cancel.
- Delete prompt → confirm `Delete "<title>"?`.
- Every successful change → `AppHost.SaveLibrary()` (tray menu rebuilds).
- Closing window hides it (app stays in tray).

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Build** → 0 errors.
- [ ] **Step 3: Manual verify:** add category `Q&A` with prompt `Ask & answer` → quick menu shows `Q&A` › `Ask & answer` literally; search `xunit` in All prompts finds "Write unit tests"; delete `Testing` → move dialog → prompts land in chosen category; edit text, switch prompt → Save changes? prompt appears; close window → app stays in tray; edit prompt with blank line + tabs → Copy → paste in Notepad identical.
- [ ] **Step 4: Run** `dotnet test` → PASS.
- [ ] **Step 5: Commit** `feat: three-column main window`.

---

### Task 10: Settings, backup scheduling, restore

**Files:**
- Create: `Views/SettingsWindow.xaml(.cs)`, `Views/RestoreWindow.xaml(.cs)`
- Modify: `AppHost.cs`, `Views/MainWindow.xaml` (no-backup-folder warning bar)

**Interfaces:**
- Consumes: `BackupService`, `LibraryHash`, `HotkeyGesture`, `StartupRegistration`, `GlobalHotkey`.
- Produces: `AppHost.RunBackup(BackupTrigger trigger)`; `AppHost.OpenRestore()`.

Behaviour:
- **Settings window:** shortcut capture box (press keys → shows `HotkeyGesture.ToString()`; invalid combos rejected with hint text), backup folder + Browse (`Microsoft.Win32.OpenFolderDialog`), backups to keep (1–365), Start with Windows checkbox. Save → `ApplyHotkey` (on failure keep old hotkey and show `That shortcut is in use by another app.`), `StartupRegistration.Apply`, `SaveSettings()`.
- **First-run backup folder:** if `settings.BackupFolder` is null and `G:\My Drive` exists, pre-fill `G:\My Drive\Prompt Library Backups` in Settings and open Settings once on first run; otherwise open Settings with an empty folder.
- **Warning bar** in main window when `BackupFolder` is null: `Backups are off — choose a Google Drive folder in Settings.` with a `Settings…` link.
- **RunBackup(trigger):** if folder null → return; hash `library.json`; if `ShouldBackup` → `BackupNow`; success → set `LastBackupAt`, `LastBackedUpLibraryHash`, save settings, (Manual only) tray message `Backup saved.`; failure → tray message `Backup failed: <error>` (error flag), settings untouched.
- **Triggers:** `DispatcherTimer` every 60 minutes → `RunBackup(Daily)`; also once 1 minute after startup. `ExitApp()` → `RunBackup(OnExit)` before shutdown. File → Back up now → `RunBackup(Manual)`.
- **Restore window:** list `BackupService.List` (date, prompt count; unreadable shown as `unreadable` and disabled), Restore button → confirm `Replace your current library with the backup from <date>? Your current library will be backed up first.` → `Restore`; success → reload library into `AppHost`, refresh UI and tray; failure → show error. Also wire the Task 8 unreadable-library startup path to open this window.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Build** → 0 errors.
- [ ] **Step 3: Manual verify:** set backup folder to a temp folder → Back up now creates `PromptLibrary-backup-…json`; Back up now again with no changes still creates one (manual); exit with a change → new backup; exit without change → none; set folder to `Z:\nope` → Back up now shows failure message, app keeps running; change hotkey to `Ctrl+Alt+K` → works immediately, old one no longer opens menu; Restore an older backup → library replaced and an extra backup appears; corrupt a backup by hand → shows `unreadable`, can't restore; corrupt `library.json` with app closed → start shows error + restore offer, file not overwritten.
- [ ] **Step 4: Run** `dotnet test` → PASS.
- [ ] **Step 5: Commit** `feat: settings, scheduled backups, restore`.

---

### Task 11: Publish and checklist

**Files:**
- Create: `docs/manual-test-checklist.md`
- Modify: `src/PromptLibrary.App/PromptLibrary.App.csproj` (publish properties), `README.md`

- [ ] **Step 1:** Add to App csproj: `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`, `<SelfContained>true</SelfContained>`, `<PublishSingleFile>true</PublishSingleFile>`, `<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>` (publish-only via a `Condition` on `'$(Configuration)'=='Release'` if it interferes with `dotnet run`).
- [ ] **Step 2: Run** `dotnet publish src/PromptLibrary.App -c Release -o publish` → `publish/PromptLibrary.App.exe` exists (single file, plus optional `.pdb`).
- [ ] **Step 3:** Write `docs/manual-test-checklist.md` — the manual checks from Tasks 8–10 plus: paste test in Windows Terminal, Chrome, VS Code, Claude Code and Claude desktop; reboot → app starts in tray.
- [ ] **Step 4:** Update `README.md`: what it is, how to build (`dotnet build`), run (`dotnet run --project src/PromptLibrary.App`), publish command, where data lives, how backups work (install Google Drive for desktop, pick folder in Settings), default shortcut.
- [ ] **Step 5:** Add `publish/` to `.gitignore`.
- [ ] **Step 6: Run** the published exe through the checklist.
- [ ] **Step 7: Commit** `chore: single-file publish, README, manual test checklist`.

# Team Prompts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship v1.1.0 "Team prompts". Each person can connect one shared, read-only team library (a folder,
synced cloud folder or public https link) next to their own prompts. People with write access edit it in place,
saves are guarded against clobbering, and history is kept.

**Architecture:**
- The pure logic lives in `PromptLibrary.Core/Team`, unit-tested: the file format, reading from a path or URL, the
  offline cache, guarded saves with history, create and join, markers, pins and hides, and the combined menu and search.
- `PromptLibrary.App` gets a `TeamLibraryService` (refresh, watcher, status) owned by `AppHost`.
- The main window works on an `ILibraryDocument`, either the personal library or the team library. The existing
  editor, list and rail code then serves both sides, and team edit mode is the same code with saves routed to
  `TeamWriter`.

**Tech Stack:** .NET 10, WPF + WinForms tray, System.Text.Json (`JsonNode` for unknown-field round-trips),
HttpClient, FileSystemWatcher, xUnit 2.9.

**Spec:** `docs/superpowers/specs/2026-11-05-team-prompts-design.md` (owner-approved; owner delegated all
further decisions)

## Global Constraints

- **Copy (exact):**
  - Switch: "Your prompts" | "Team prompts". Editor lock line: "Team prompt · {Name} · read only". Edit bar:
    "Editing team library: changes go to everyone" + **Done**.
  - Buttons: **Copy prompt** (the only filled button), **Copy to your prompts**, **Pin** / **Unpin**,
    **Hide** / **Unhide**, **Edit team library**, **Join a team library**, **Start a team library**,
    **Leave team library**, **Retry**, **Mark all as seen**, "Show hidden ({n})".
  - Messages: "Copied to your prompts · {Category}"; "This removes it for everyone. Delete?"; "Someone else
    changed the team library. It's been reloaded. Please make your change again."; "This team library was made
    with a newer Prompt Library. Update to edit it."; "Can't reach that folder. Is {Provider} running?" /
    "Can't reach that folder."; "That isn't a Prompt Library team file."; "This folder already has a team
    library ({Name})." + **Join it**; header line "{Name} · updated {when}" / "Last updated {when} · can't reach
    the team library · Retry"; editor notice "Two versions of the team library found" + **Review**.
- **Files:**
  - Team file name `team-prompts.json`, with history in a `Prompt Library history` folder next to it (keep 30).
  - The offline copy is `%APPDATA%\PromptLibrary\team-cache.json`.
  - JSON root: `kind: "prompt-library-team"`, `schemaVersion: 1`, `name`, `updatedAt`, `categories`, `prompts`
    (the same shapes as `library.json`).
- **Refresh:** at start, every 60 min, on a debounced (2 s) file change for path sources, and on Retry. Held
  while edit mode is on, then applied on Done. Never blocks the UI or the shortcut menu.
- **URL:** https only, no sign-in, a 15 s timeout, a 2 MB body cap, the `PromptLibrary/{version}` User-Agent, no
  redirect to non-https.
- **Shortcut menu:** "Update to X…", then Pinned (flat, in pin order), then team categories (team icon; hidden
  prompts left out), then a separator, then your categories, then a separator, then Settings…, Open…, Exit.
- **Keyboard:** Ctrl+1 shows Your prompts, Ctrl+2 shows Team prompts.
- **Solo users:** no switch, no team block and no Pinned block until joined, started or pinned.
- **Design bar:** `docs/design/ui-style.md`; rubric quality ≥ 90 % on every new or changed screen;
  simplicity ≥ 95 % on the main window (both sides) and the setup guide, ≥ 90 % on dialogs; the spec's team core
  tasks within their action counts. Results go in `docs/design/ui-review-results.md`.
- **Testing:**
  - Live runs only with `PROMPTLIBRARY_DEBUG_NO_CLOUDS=1`, with stand-in team folders under the scratchpad.
  - The owner's `%APPDATA%\PromptLibrary` is backed up and restored, and `startWithWindows` is false during runs.
  - Never touch the real OneDrive or `G:\`.

- **Spec additions after independent scoring (1–10) are binding.** Where they differ from this plan, they win:
  - names **Make my copy** and **Hide from menu**
  - the button-row table, the header-line layout, and **Show hidden** under search
  - **Leave before changing library**
  - menu headings, collapsing into a **Team prompts ▸** submenu when there are more than 6 team categories, and a
    **pin cap of 10**
  - the link rewrites and the web-page error
  - the **setup step 3 of 5**
  - the **business example set** and business demo data ("Acme best practice")

## Review Focus

1. **A team file mid-sync, truncated or replaced by garbage** must never replace the last good copy or crash.
   Test: `Read_TruncatedJson_KeepsCacheAndReportsUnreadable` (Task 2).
2. **An editor on an older build saving a newer file** must not strip unknown fields. Edit is off for
   `schemaVersion > 1`, and unknown fields survive a v1 save. Tests: `Save_PreservesUnknownFields`,
   `NewerSchema_IsReadOnly` (Tasks 1, 3).
3. **Start a team library in a folder that already has one** must never overwrite it. Test:
   `Create_ExistingFile_RefusesAndReturnsItsName` (Task 3).
4. **Owner deletes a prompt that members pinned, hid or had marked** must drop it silently, with no ghost menu
   item. Test: `Prune_DropsDanglingPinsHidesAndSeen` (Task 4).
5. **A team category named like one of yours** ("Sales" in both) must stay separate in the menu and search, with
   the team one carrying the team icon, and Copy to your prompts landing in your "Sales". Tests:
   `Build_SameNamedCategories_StaySeparate`, `CopyToYours_UsesSameNamedCategory` (Tasks 5, 8).

---

### Task 1: Team file format

**Files:**
- Create: `src/PromptLibrary.Core/Team/TeamFile.cs`, `TeamLibrary.cs`
- Test: `tests/PromptLibrary.Core.Tests/Team/TeamFileTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class TeamLibrary { string Name; DateTimeOffset UpdatedAt; int SchemaVersion; Library Content; bool IsNewerSchema; JsonObject Raw; }`.
    `Raw` keeps unknown fields for round-tripping.
  - `public static class TeamFile`:
    - `const string FileName = "team-prompts.json"`, `const string Kind = "prompt-library-team"`.
    - `static TeamLibrary Parse(string json)` throws `InvalidDataException` with plain messages.
    - `static string Serialize(TeamLibrary team)` writes `Content`, `Name` and `UpdatedAt` into a clone of `Raw`, so
      unknown root, category and prompt fields survive.
    - `static string Hash(string json)` returns the SHA-256 hex of the normalised text.

- [ ] **Step 1: Failing tests:**
  - `Parse_ValidFile_ReadsNameCategoriesPrompts`
  - `Parse_WrongKindOrMissing_Throws`
  - `Parse_BadJson_Throws`
  - `Parse_PromptWithMissingCategory_Throws` (reuses `JsonStore` rules)
  - `NewerSchema_IsReadOnly` (`schemaVersion: 2` with valid v1 fields → `IsNewerSchema == true`, content readable)
  - `Save_PreservesUnknownFields` (root `"x":1`, a prompt with `"tags":["a"]` → serialize after editing the
    prompt's text → both still present)
  - `Serialize_EmojiAndCrlf_RoundTrip` (same escaping as `JsonStore`)
- [ ] **Step 2:** `dotnet test tests/PromptLibrary.Core.Tests --filter FullyQualifiedName~Team` → compile fail.
- [ ] **Step 3: Implement.**
  - Parse with `JsonNode`, validate as `JsonStore.ParseLibrary` does (share its validation by extracting
    `JsonStore.ValidateLibrary(JsonElement)`), then deserialize `Content`.
  - Serialize by updating the matching nodes in a deep clone of `Raw`, matched by `id`; prompts and categories
    removed or added are removed or added in the arrays.
- [ ] **Step 4:** The same filter → PASS, and the full Core suite stays green.
- [ ] **Step 5:** Commit `feat(team): team library file format with unknown-field round-trip`.

### Task 2: Sources, reading and offline copy

**Files:**
- Create: `src/PromptLibrary.Core/Team/TeamSource.cs`, `TeamReader.cs`, `TeamCache.cs`
- Test: `tests/PromptLibrary.Core.Tests/Team/TeamSourceTests.cs`, `TeamReaderTests.cs`, `TeamCacheTests.cs`

**Interfaces:**
- Consumes: `TeamFile` (Task 1); test helper `FakeHandler` (`tests/.../Updates/FakeHandler.cs`).
- Produces:
  - `public sealed record TeamSource(TeamSourceKind Kind, string Location)` with `enum TeamSourceKind { File, Url }`.
    `static TeamSource? FromInput(string input)` turns a folder into `folder\team-prompts.json`, keeps a file path,
    and accepts an https URL. It returns null for http, other schemes or empty input.
  - `public sealed record TeamReadResult(TeamReadStatus Status, TeamLibrary? Team, string? Json, string? Hash, string? Message)`
    with `enum TeamReadStatus { Ok, Unreachable, NotATeamFile, TooNew }`.
    `TooNew` means not even v1-readable; a file that is newer but readable is `Ok` with `IsNewerSchema`.
  - `public sealed class TeamReader(HttpClient http)` with `Task<TeamReadResult> ReadAsync(TeamSource source, CancellationToken ct)`.
  - `public static class TeamCache`:
    - `static void Save(string path, TeamSource source, string json, DateTimeOffset fetchedAt)`
    - `static (TeamSource Source, TeamLibrary Team, string Json, DateTimeOffset FetchedAt)? Load(string path)`
    - `static void Delete(string path)`

- [ ] **Step 1: Failing tests:**
  - `FromInput_Folder_File_Https_Http_Junk`
  - `Read_File_Ok`
  - `Read_MissingFolder_Unreachable`
  - `Read_TruncatedJson_KeepsCacheAndReportsUnreadable`: a reader result `NotATeamFile`; the cache on disk is
    unchanged because the caller only saves on `Ok`. Assert the cache is not saved by the helper
    `TeamRefresh.Apply` (below).
  - `Read_Url_Ok_SendsUserAgent`
  - `Read_Url_Over2MB_NotATeamFile`
  - `Read_Url_Timeout_Unreachable`
  - `Read_Url_404_Unreachable`
  - `Cache_RoundTrip`
  - `Cache_Corrupt_ReturnsNull`
- [ ] **Step 2:** Run → fail.
- [ ] **Step 3: Implement.**
  - File reads retry once after 500 ms on `IOException` (a sync tool may be writing).
  - The URL read streams up to the cap. The client's own redirects must stay https: check
    `response.RequestMessage.RequestUri.Scheme`.
  - Add `public static class TeamRefresh { static bool Apply(TeamReadResult r, string cachePath, TeamSource s, DateTimeOffset now) }`.
    It saves the cache only on `Ok` and returns whether the library changed (hash differs).
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(team): team sources, reading and offline copy`.

### Task 3: Writing, history, create and conflicts

**Files:**
- Create: `src/PromptLibrary.Core/Team/TeamWriter.cs`
- Test: `tests/PromptLibrary.Core.Tests/Team/TeamWriterTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2.
- Produces: `public sealed class TeamWriter(Func<DateTimeOffset> now)`.
  - `static bool CanWrite(string filePath)`: open for write with `FileShare.ReadWrite` and close, without
    changing the file. False on any IO or permission error.
  - `TeamSaveResult Save(string filePath, TeamLibrary team, string expectedHash)`, with
    `enum TeamSaveStatus { Saved, ChangedByOther, Failed }` and
    `record TeamSaveResult(TeamSaveStatus Status, string? NewHash, string? Message)`.
    1. Re-read; a different hash means `ChangedByOther`.
    2. Copy to `Prompt Library history\team-prompts {yyyy-MM-dd_HHmmss}.json` and keep the 30 newest.
    3. Set `UpdatedAt = now()` and write with `AtomicFile`.
  - `IReadOnlyList<TeamHistoryItem> ListHistory(string filePath)` (newest first) and
    `TeamSaveResult Restore(string filePath, TeamHistoryItem item, string expectedHash)`, which goes through `Save`,
    so a restore also makes a history copy.
  - `TeamCreateResult Create(string folder, string name, Library? seedFrom, IReadOnlyCollection<Guid> seedCategoryIds, bool addBusinessExamples)`
    with `record TeamCreateResult(bool Created, string FilePath, string? ExistingName, string? Message)`.
  - `public static class TeamSeedLibrary { static Library Create(DateTimeOffset now) }` in
    `src/PromptLibrary.Core/Team/TeamSeedLibrary.cs` holds the spec's 24 business prompts in 6 categories (4 each,
    exact category names from spec addition 10). It is tested like `SeedLibraryTests`:
    - `TeamSeed_24Prompts_6Categories_4Each`
    - `TeamSeed_UniqueTitles_PlaceholdersPresent_TitlesUnder40`
    - `TeamSeed_HrFinancePolicyPrompts_CarryCheckNote` (the HR & people and Operations & finance categories contain
      "check with your")
    - `Create_WithBusinessExamples_SeedsTheSet`
  - `static IReadOnlyList<string> FindConflictCopies(string filePath)` returns sibling files whose names start with
    `team-prompts` and end with `.json`, other than the file itself and `.tmp`/`.bak` (OneDrive's
    "team-prompts-PCNAME.json" and Dropbox's "(conflicted copy)").

- [ ] **Step 1: Failing tests:**
  - `Save_Unchanged_WritesAndKeepsHistory`
  - `Save_ChangedSinceLoad_ReturnsChangedByOther_AndWritesNothing`
  - `Save_KeepsNewest30History`
  - `Restore_WritesOldVersion_AndHistoryOfCurrent`
  - `Create_NewFolder_WritesFileWithFreshIds` (the seed categories and prompts get new GUIDs, and the seed
    library is unchanged)
  - `Create_ExistingFile_RefusesAndReturnsItsName`
  - `CanWrite_ReadOnlyFile_False`
  - `FindConflictCopies_OneDriveAndDropboxNames`
- [ ] **Step 2:** Run → fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(team): guarded team saves with history, create and conflict detection`.

### Task 4: Per-PC state: pins, hides, markers

**Files:**
- Create: `src/PromptLibrary.Core/Team/TeamUserState.cs`
- Modify: `src/PromptLibrary.Core/Models/AppSettings.cs`, adding:
  - `string? TeamSource`
  - `List<Guid> PinnedPromptIds`
  - `List<Guid> HiddenTeamPromptIds`
  - `Dictionary<Guid,string> SeenTeamPrompts` (id → fingerprint)
  - `bool TeamBaselineTaken`
- Test: `tests/PromptLibrary.Core.Tests/Team/TeamUserStateTests.cs`

**Interfaces:**
- Produces: `public static class TeamUserState`.
  - `static string Fingerprint(Prompt p)`: a hash of title, description, text and category ID.
  - `static void TakeBaseline(AppSettings s, Library team)`: marks everything seen on first join.
  - `static TeamMarker MarkerFor(AppSettings s, Prompt p)` with `enum TeamMarker { None, New, Updated }`.
  - `static void MarkSeen(AppSettings s, Prompt p)` and `static void MarkAllSeen(AppSettings s, Library team)`.
  - `static bool HasUnseen(AppSettings s, Library team)`.
  - `static void Pin(AppSettings s, Guid id)` (appends; unhides if hidden),
    `static void Unpin(AppSettings s, Guid id)`, and `static void Hide(AppSettings s, Guid id)` (also unpins).
  - `static void Unhide(AppSettings s, Guid id)`.
  - `static bool Prune(AppSettings s, Library personal, Library? team)` drops IDs not in either library and returns
    whether anything changed.
  - `static void ClearTeam(AppSettings s, Library? team)` (leave or switch source) removes team pins, hides, seen
    entries and the baseline flag, and keeps personal pins.

- [ ] **Step 1: Failing tests:**
  - `FirstJoin_NothingMarked`
  - `NewPrompt_IsNew_EditedText_IsUpdated_CategoryRename_None`
  - `MarkSeen_Clears`
  - `MarkAllSeen_ClearsAll`
  - `Hide_Unpins`
  - `Pin_KeepsOrder`
  - `Prune_DropsDanglingPinsHidesAndSeen`
  - `ClearTeam_KeepsPersonalPins`
  - `Settings_RoundTrip_NewFields` (with `JsonStore.SaveSettings`/`LoadSettings`; old settings files without
    the fields still load)
- [ ] **Step 2:** Run → fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(team): pins, hides and new/updated markers`.

### Task 5: Combined shortcut menu and search

**Files:**
- Modify: `src/PromptLibrary.Core/Menu/QuickMenuBuilder.cs`, `src/PromptLibrary.Core/Search/PromptFilter.cs`
- Test: `tests/PromptLibrary.Core.Tests/QuickMenuBuilderTests.cs` (existing), `tests/PromptLibrary.Core.Tests/Team/CombinedSearchTests.cs`

**Interfaces:**
- Consumes: Task 4.
- Produces:
  - `public record QuickMenuPrompt(Guid Id, string Title, bool IsTeam = false)`.
  - `public record QuickMenu(IReadOnlyList<QuickMenuPrompt> Pinned, IReadOnlyList<QuickMenuCategory> Team, IReadOnlyList<QuickMenuCategory> Yours)`.
  - `static QuickMenu QuickMenuBuilder.Build(Library personal, Library? team, AppSettings s)`. The old
    `Build(Library)` stays as `Build(lib, null, new())`.Yours, so the existing tests keep passing.
  - `public sealed record SearchHit(Prompt Prompt, bool IsTeam, bool IsHidden)`.
  - `static IReadOnlyList<SearchHit> PromptFilter.Search(Library personal, Library? team, AppSettings s, string query)`
    returns yours first, then the team's, each A–Z. Hidden team prompts are included with `IsHidden`.

- [ ] **Step 1: Failing tests:**
  - `Build_NoTeamNoPins_SameAsBefore`
  - `Build_PinnedFirstInPinOrder_TeamFlagged`
  - `Build_HiddenTeamPromptsLeftOut`
  - `Build_SameNamedCategories_StaySeparate`
  - `Search_BothLibraries_LabelsTeam_IncludesHiddenGreyed`
- [ ] **Step 2:** Run → fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run → PASS (the whole Core suite).
- [ ] **Step 5:** Commit `feat(team): combined shortcut menu and search`.

### Task 6: TeamLibraryService and AppHost wiring

**Files:**
- Create: `src/PromptLibrary.App/Team/TeamLibraryService.cs`
- Modify: `src/PromptLibrary.App/AppHost.cs` (create, start, dispose; `RefreshQuickMenu` uses the new builder),
  `src/PromptLibrary.App/Paths.cs` (`TeamCacheFile`)
- Test: `tests/PromptLibrary.App.Tests/TeamLibraryServiceTests.cs` (STA helper `Sta.cs` exists; drive with a temp
  folder source and a fake clock)

**Interfaces:**
- Consumes: Tasks 1–5.
- Produces: `public sealed class TeamLibraryService : IDisposable`.
  - `TeamLibrary? Team`, `TeamSource? Source`, `string? CurrentHash`, `DateTimeOffset? LastUpdated`,
    `TeamStatus Status` (`Connected | Offline | NotConnected`), `bool CanEdit` (`File` source, `CanWrite`,
    `!IsNewerSchema`), `IReadOnlyList<string> ConflictCopies`.
  - `event EventHandler? Changed` (on the UI thread).
  - `Task RefreshAsync()`, `Task<TeamJoinResult> JoinAsync(string input)` (validates before saving the source;
    clears the old team state), `void Leave()`, and `TeamCreateResult Start(string folder, string name, IReadOnlyCollection<Guid> seedCategoryIds)`.
  - `void HoldRefresh(bool hold)`.
  - `TeamSaveResult Save(TeamLibrary edited)`. On `ChangedByOther` it reloads and raises `Changed`.
  - Startup loads the cache first (instant), then refreshes.

- [ ] **Step 1: Failing tests:**
  - `Join_InvalidFile_DoesNotSaveSource`
  - `Join_Valid_SavesSourceTakesBaselineRaisesChanged`
  - `Refresh_SourceGone_KeepsTeamStatusOffline`
  - `HoldRefresh_DefersUntilReleased`
  - `Save_ChangedByOther_Reloads`
  - `Leave_ClearsCacheAndTeamState`
- [ ] **Step 2:** Run → fail.
- [ ] **Step 3: Implement.** Hourly `DispatcherTimer`, a `FileSystemWatcher` on the source folder (filter
  `team-prompts*.json`, debounce 2 s), and a shared `HttpClient` (the one from `AppHost`).
- [ ] **Step 4:** Run → PASS, then build 0/0.
- [ ] **Step 5:** Commit `feat(team): team library service and app wiring`.

### Task 7: Tray and shortcut menu

**Files:**
- Modify: `src/PromptLibrary.App/Tray/TrayIcon.cs` (`Rebuild(QuickMenu menu)`), `AppHost.cs` (prompt picks look
  in both libraries)

**Interfaces:**
- Consumes: `QuickMenu` (Task 5). Team items show a 16 px team icon (`Assets/team.png`, added as a Resource,
  drawn in the app's Ink colour). Pinned items show a pin icon.

- [ ] **Step 1:** `QuickMenuBuilderTests` already pins the order. Add App test `Tray_Rebuild_OrderMatchesQuickMenu`
  (STA; read `ContextMenuStrip.Items` texts and separators). It must fail first.
- [ ] **Step 2:** Implement. `OnPromptPicked` resolves the ID in the personal library first, then the team's.
- [ ] **Step 3:** Run → PASS. Commit `feat(team): pinned and team blocks in the shortcut menu`.

### Task 8: Main window: switch, Team side, pin, hide, markers, search

**Files:**
- Create: `src/PromptLibrary.App/ViewModels/ILibraryDocument.cs`, `PersonalDocument.cs`, `TeamDocument.cs`
- Modify: `ViewModels/MainViewModel.cs`, `ViewModels/PromptEditorViewModel.cs` (`IsReadOnly`),
  `Views/MainWindow.xaml(.cs)`
- Test: `tests/PromptLibrary.App.Tests/MainViewModelTeamTests.cs`

**Interfaces:**
- Produces:
  - `public interface ILibraryDocument { Library Library { get; } bool IsTeam { get; } bool IsReadOnly { get; } bool SaveLibrary(); bool SaveLibraryChange(); bool LibraryUnwritten { get; } }`.
  - `PersonalDocument` wraps `AppHost` (the exact current behaviour).
  - `TeamDocument` wraps `TeamLibraryService`. `IsReadOnly` is true unless edit mode is on, and Save goes to
    `TeamLibraryService.Save`.
  - `MainViewModel`:
    - `LibrarySide Side` (`Yours | Team`), `bool ShowSwitch`, `ICommand ShowYoursCommand` (Ctrl+1) and
      `ShowTeamCommand` (Ctrl+2), `string? TeamHeaderText`, `bool TeamHasUnseen`.
    - `ICommand CopyToYoursCommand`, `PinCommand`, `HideCommand`, `MarkAllSeenCommand`, `RetryTeamCommand`.
    - `bool ShowHiddenTeamPrompts`, `int HiddenCount`.
    - Every former `_host.Library` / `_host.SaveLibrary*` goes through `CurrentDocument`.

- [ ] **Step 1: Refactor first, no behaviour change.** Route the view model through `ILibraryDocument`
  (`PersonalDocument` only). The full suite stays green, and the app is smoke-run once.
- [ ] **Step 2: Failing tests** (with a fake `TeamLibraryService` or temp folder):
  - `TeamSide_EditorIsReadOnly`
  - `CopyToYours_UsesSameNamedCategory`
  - `CopyToYours_CreatesCategoryWhenMissing`
  - `Hide_RemovesFromListAndUnpins`
  - `ShowHidden_ListsGreyedWithUnhide`
  - `Search_FindsTeamPrompt_OpensOnTeamSide`
  - `Open_ClearsMarker`
  - `NoTeam_NoSwitch`
- [ ] **Step 3: Implement the view model**, then the XAML:
  - a segmented switch over the three columns
  - the header line
  - the lock line above the slip
  - a read-only slip that shows normal text
  - **Pin**, **Hide** and **Copy to your prompts** in the button row
  - New/Updated tags and a pin glyph in rows
  - the team icon in search results
  - **Show hidden ({n})** and **Mark all as seen** only when relevant
  - the row right-click menu (Pin/Hide)
- [ ] **Step 4:** Run → PASS, build 0/0. Commit `feat(team): team side in the main window`.

### Task 9: Team edit mode, history and conflict copies

**Files:**
- Modify: `MainViewModel.cs`, `MainWindow.xaml`, `TeamDocument.cs`
- Create: `src/PromptLibrary.App/Views/TeamHistoryWindow.xaml(.cs)` (the Restore window's layout: list of
  versions, **Restore**, **Cancel**)
- Test: `tests/PromptLibrary.App.Tests/TeamEditModeTests.cs`

**Interfaces:**
- Consumes: `TeamLibraryService.CanEdit/Save/HoldRefresh/ConflictCopies` and `TeamWriter.ListHistory/Restore`.
- Produces: `MainViewModel.IsEditingTeam`, `EditTeamCommand`, `DoneEditingTeamCommand`, `TeamHistoryCommand`,
  `ReviewConflictCommand`.

- [ ] **Step 1: Failing tests:**
  - `EditButton_OnlyWhenCanEdit`
  - `EditMode_HoldsRefresh_DoneReleases`
  - `Delete_AsksRemovesForEveryone`
  - `Save_ChangedByOther_KeepsTypedText_ShowsMessage`
  - `NewerSchema_NoEditButton_ShowsReason`
- [ ] **Step 2: Implement.**
  - The edit bar reuses the warning-bar style.
  - The history window opens from the Team side's "…" menu ("Restore an older version…").
  - The conflict copies notice reuses the bar with **Review**, which opens the history window listing the conflict
    copies first.
- [ ] **Step 3:** Run → PASS. Commit `feat(team): edit the team library in place, with history and conflict review`.

### Task 10: Settings, Join/Start dialogs, setup-guide step

**Files:**
- Modify: `Views/SettingsWindow.xaml(.cs)` (a Team library section), `Core/Onboarding/OnboardingFlow.cs`
  (the steps become Welcome → Shortcut → Team → Backups → Ready, `StepCount = 5`), `OnboardingViewModel.cs`,
  `OnboardingWindow.xaml`
- Create: `Views/TeamJoinDialog.xaml(.cs)` (one box "Shared folder, file or link" + **Browse…** + **Join**),
  `Views/TeamStartDialog.xaml(.cs)` (folder + name + a collapsed "Copy some of your categories in" checklist +
  **Start**)
- Test: `tests/PromptLibrary.Core.Tests/OnboardingFlowTests.cs` (update the step numbers and add
  `TeamStep_SkipKeepsNoTeam`)

- [ ] **Step 1:** Update the onboarding tests (failing).
- [ ] **Step 2: Implement.**
  - Join and Start call the service. Errors are inline in the dialog, in `Danger` text, using the exact copy
    above.
  - "Already has a team library" shows **Join it**.
  - **Leave team library** confirms "Leave {Name}? Team prompts will disappear; your copies stay."
- [ ] **Step 3:** Run → PASS. Commit `feat(team): join, start and leave a team library`.

### Task 11: Docs, UI review, live run, version

**Files:**
- Modify:
  - `README.md` (a Team prompts feature row and the privacy line)
  - `docs/DEVELOPMENT.md` (a Team library section: file format, history, cache, refresh, testing)
  - `docs/manual-test-checklist.md` (a Team prompts section)
  - `docs/design/ui-review-results.md` (new scores)
  - `src/PromptLibrary.App/PromptLibrary.App.csproj` (`<Version>1.1.0</Version>`) and the README badge
- [ ] **Step 1: Live run** (owner AppData backed up, NO_CLOUDS, two stand-in team folders, with one made
  read-only). Members join, see, copy, pin, hide and search. An editor edits, saves and makes a clobber attempt.
  Then cover offline and Leave.
- [ ] **Step 2: Screenshots.** Score the quality and simplicity rubrics for every new or changed screen, and fix
  until the bar is met.
- [ ] **Step 3:** Build 0/0, all tests pass. Bump to 1.1.0. Commit `docs: team prompts; release 1.1.0`.

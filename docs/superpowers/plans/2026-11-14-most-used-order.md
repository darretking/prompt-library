# Most-used order and Pinned row Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sort prompts and categories by all-time use count (A–Z ties) in the tray menu, search window and main window, and add a Pinned row to the main window rail.

**Architecture:** One pure Core helper (`UsageOrder`) owns the ordering rule and the `PromptSort` switch; every list builder calls it instead of `OrderBy(Title)`. The main window gains a pseudo-category row (like Recent/Featured). Settings gets a two-option radio and a Clear usage history button.

**Tech Stack:** .NET 10, WPF + WinForms tray, CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-14-most-used-order-design.md` (rulings U1–U12).

## Global Constraints

- Branch `feature/most-used-order` from `main` @ 048c6ee. Parallel tasks use hand-made worktrees from that branch (`git worktree add ../pl-tN -b task/N feature/most-used-order`); Agent-tool worktrees start from `main`, so don't use `isolation: "worktree"`.
- Windows 11, Git Bash. Tests: `dotnet test tests/PromptLibrary.Core.Tests` and `dotnet test tests/PromptLibrary.App.Tests`.
- Comparer for names everywhere: `StringComparer.CurrentCultureIgnoreCase`.
- `PromptSort` values: `"used"` (default) / `"name"`; null or unknown → `"used"`.
- Copy is exact as in the spec (U1, U10). No new NuGet packages. No new emoji assets.
- Never run the real app against `%APPDATA%\PromptLibrary`; tests use fakes/temp folders only.
- Don't touch the owner's OneDrive/Google Drive.

## Review Focus

1. Fresh install / no usage → every list identical to 1.3.0 A–Z (except pins, now A–Z per U6). Test in Task 1 and Task 2.
2. 1.3.0 settings.json with no `PromptSort` key → `"used"`. Test in Task 1 (JSON round-trip).
3. Usage entries for deleted prompts and pins for hidden/absent team prompts → ignored, no crash. Tests in Task 2 and Task 4.
4. Equal counts, case-only different titles → stable A–Z. Test in Task 1.
5. Pinned row disappears when the last pin on that side is removed while it is selected → selection falls back to All prompts. Test in Task 4.

---

## Model assignment

| Task | Model | Why |
|---|---|---|
| 1 Core `UsageOrder` + setting | Sonnet | Small pure logic + tests; clear spec. |
| 2 Tray menu order + refresh after use | Sonnet | Edits a builder with existing tests; touches two host files. |
| 3 Search window groups | Sonnet | One method in a view model + tests. |
| 4 Main window Pinned row + list order | Sonnet | Biggest task, but follows the Recent/Featured pattern exactly; Opus not needed. |
| 5 Settings: Prompt order + Clear history | Sonnet | WPF section copying existing radio/confirm patterns. |
| 6 Changelog, README, version, gallery | Sonnet | Text + version are mechanical, but gallery renders need rail selection and Settings scrolling — beyond Haiku. |
| 7 Branch review + UI critic | Opus | Independent judgment; UI critics always Opus (≥90% bar). |
| 8 Full test run | Haiku (test-runner) | Only failures matter. |

Order: 1 → (2, 3, 4, 5 in parallel, disjoint files) → 6 → 7 → fixes (Sonnet) → 8. Orchestrator merges task branches.

---

### Task 1: `UsageOrder` and `PromptSort`

**Files:**
- Create: `src/PromptLibrary.Core/Usage/UsageOrder.cs`
- Modify: `src/PromptLibrary.Core/Models/AppSettings.cs` (add property after `Theme`)
- Test: `tests/PromptLibrary.Core.Tests/Usage/UsageOrderTests.cs`, `tests/PromptLibrary.Core.Tests/SettingsNullSafetyTests.cs`

**Interfaces — Produces:**
- `AppSettings.PromptSort { get; set; }` string, null-safe setter (pattern of `Theme`), default `"used"`.
- `static class UsageOrder` in `PromptLibrary.Core.Usage`:
  - `public const string ByUse = "used", ByName = "name";`
  - `public static bool SortsByUse(AppSettings s)` → `s.PromptSort != ByName`.
  - `public static int Uses(AppSettings s, Guid promptId)` → `Usage[id]?.Count ?? 0` (missing or null → 0).
  - `public static IReadOnlyList<T> Sort<T>(IEnumerable<T> items, Func<T, int> uses, Func<T, string> name, AppSettings s)` — by-use: `uses` desc then `name` A–Z; by-name: `name` A–Z only. Stable.
  - `public static IReadOnlyList<Prompt> Prompts(IEnumerable<Prompt> prompts, AppSettings s)` → `Sort(prompts, p => Uses(s, p.Id), p => p.Title, s)`.

- [ ] **Step 1: Write failing tests** in `UsageOrderTests`:
  - `Prompts_NoUsage_IsAToZ` — titles `"b","A","c"` → `A,b,c`.
  - `Prompts_ByUse_MostUsedFirst_TiesAToZ` — counts b=3, c=3, A=0 → `b,c,A`.
  - `Prompts_ByName_IgnoresUsage` — `PromptSort="name"`, b=5 → `A,b,c`.
  - `Uses_MissingOrNullEntry_IsZero` — `Usage[id]=null!` → 0.
  - `SortsByUse_UnknownValue_IsTrue` — `"foo"` → true.
  - `Prompts_CaseOnlyDifferentTitles_StableOrder` — `"a"` then `"A"` (equal under comparer) keep input order.
  - In `SettingsNullSafetyTests`: `PromptSort_Null_BecomesUsed`, and JSON `{}` deserialized via `JsonStore` loader yields `"used"` (follow the file's existing pattern).
- [ ] **Step 2:** `dotnet test tests/PromptLibrary.Core.Tests --filter "UsageOrder|SettingsNullSafety"` → FAIL (type missing).
- [ ] **Step 3:** Implement the two files. Use `OrderByDescending(uses).ThenBy(name, comparer)` (LINQ is stable).
- [ ] **Step 4:** Same command → PASS; then full Core tests PASS.
- [ ] **Step 5:** Commit `feat(core): usage order helper and PromptSort setting`.

---

### Task 2: Tray menu order and refresh after use

**Files:**
- Modify: `src/PromptLibrary.Core/Menu/QuickMenuBuilder.cs`
- Modify: `src/PromptLibrary.App/AppHost.cs:450-454` (`onCopied`), `src/PromptLibrary.App/Search/SearchController.cs:204-209` (`RecordUse`)
- Test: `tests/PromptLibrary.Core.Tests/QuickMenuBuilderTests.cs`

**Interfaces — Consumes:** Task 1 `UsageOrder`. **Produces:** unchanged `QuickMenu` shape; `Build(Library)` overload keeps A–Z (passes `new AppSettings { PromptSort = "name" }` internally or equivalent).

- [ ] **Step 1: Failing tests** (Core):
  - `Build_ByUse_PromptsInCategoryMostUsedFirst` — counts set on `settings.Usage` → order by count, ties A–Z.
  - `Build_ByUse_CategoriesBySumOfUses` — cat "Zed" prompts total 5, "Alpha" 0 → `Zed` first in `Yours`.
  - `Build_ByUse_TeamCategoriesSorted_HiddenNotCounted` — hidden team prompt with 10 uses doesn't lift its category.
  - `Build_ByUse_PinnedMostUsedFirst` — pins [A(0), B(4)] → `B, A`.
  - `Build_ByName_PinnedAToZ` — `PromptSort="name"`, pins added order [b, A] → `A, b`.
  - `Build_UsageForDeletedPrompt_Ignored` — usage key not in any library → no throw, menu unchanged.
  - `Build_FeaturedKeepsLibraryOrder` — featured team prompts with different counts keep library order.
  - Existing tests must still pass unmodified except `Build_PinnedFirstInPinOrder_TeamFlagged` (QuickMenuBuilderTests.cs:85-97), which asserted pin order: update it to the U6 rule and say so in the commit.
- [ ] **Step 2:** `dotnet test tests/PromptLibrary.Core.Tests --filter QuickMenuBuilder` → FAIL.
- [ ] **Step 3:** In `Categories(...)` pass `AppSettings`; sort prompts with `UsageOrder.Prompts`. `QuickMenuCategory.Name` already carries the emoji, so sort `(Category, prompts)` pairs on the raw `Category.Name` (score = sum of `Uses` of the shown prompts) *before* mapping to `QuickMenuCategory`. Sort the pinned list with `UsageOrder.Sort(pinned, p => Uses(s, p.Id), p => p.Title, s)`.
- [ ] **Step 4:** Add `_host.RefreshQuickMenu();` in `SearchController.RecordUse` and `RefreshQuickMenu();` in `AppHost` `onCopied` (after `SaveSettings()`). No test (UI host); covered by Task 7 review.
- [ ] **Step 5:** Core tests PASS, App tests build. Commit `feat(tray): most-used order and refresh after each use`.

---

### Task 3: Search window groups

**Files:** Modify `src/PromptLibrary.App/ViewModels/SearchViewModel.cs:486-513` (`AddGroups`). Test `tests/PromptLibrary.App.Tests/SearchViewModelTests.cs`.

**Interfaces — Consumes:** `UsageOrder.Sort`, `UsageOrder.Uses`.

- [ ] **Step 1: Failing tests** (use the file's existing host fake):
  - `EmptyQuery_PinnedGroup_MostUsedFirst`.
  - `EmptyQuery_AllPromptsGroup_MostUsedFirst_TiesAToZ`.
  - `EmptyQuery_ByName_AllPromptsAToZ`.
  - `EmptyQuery_RecentAndFeaturedOrderUnchanged` — Recent still most-recent-first.
- [ ] **Step 2:** `dotnet test tests/PromptLibrary.App.Tests --filter SearchViewModel` → FAIL.
- [ ] **Step 3:** Sort `pins` and `rest` with `UsageOrder.Sort(list, e => Uses(s, e.Prompt.Id), e => e.Prompt.Title, s)`; settings read from `_host.Settings` on every `Rebuild` (U11).
- [ ] **Step 4:** PASS. Commit `feat(search): most-used order for Pinned and All prompts`.

---

### Task 4: Main window Pinned row and list order

**Files:**
- Modify: `src/PromptLibrary.App/ViewModels/CategoryItem.cs` — `PinnedId` (new fixed Guid), `IsPinnedRow`, include in `IsCategory` exclusion, `ShowEmoji => !IsFeatured && !IsPinnedRow`, `RailEmoji` "" for it.
- Modify: `src/PromptLibrary.App/ViewModels/MainViewModel.cs` — `PinnedPrompts()`, `RebuildCategories` (insert row after All prompts), `RefreshPrompts` (pinned branch + search filter like Recent), `EditorSurvivesCategory`, `RefreshUserState` (refresh list when Pinned selected), line ~2646 (treat `PinnedId` like `RecentId`), list order for All prompts/category via `UsageOrder.Prompts`.
- Modify: `src/PromptLibrary.App/Views/MainWindow.xaml:470-473` — pin glyph `&#xE718;` in the tile, `Brush.Brand`, visible on `IsPinnedRow`; `AutomationProperties.Name` of row stays its Name "Pinned".
- Test: `tests/PromptLibrary.App.Tests/CategoryItemTests.cs`, new `tests/PromptLibrary.App.Tests/MainViewModelPinnedTests.cs` (copy the `FakeHost` from `MainWindowPremiumTests.cs:676`).

**Interfaces — Produces:** `CategoryItem.PinnedId`, `CategoryItem.IsPinnedRow`. `private List<Prompt> PinnedPrompts()` = current side's prompts whose id is in `PinnedPromptIds` and `IsListed`, ordered by `UsageOrder.Prompts`.

- [ ] **Step 1: Failing tests:**
  - `CategoryItem_PinnedRow_IsNotCategory_HidesEmoji`.
  - `Rail_PinnedRow_AfterAllPrompts_BeforeRecent_WhenSideHasPins` (count = pins on that side).
  - `Rail_NoPinsOnSide_NoPinnedRow` — pins only on the team side → Yours rail has no row.
  - `PinnedRow_ListsPinsMostUsedFirst`.
  - `PinnedRow_HiddenTeamPin_NotListed`.
  - `PinnedRow_UnpinLastWhileSelected_FallsBackToAllPrompts` (via `RefreshUserState`).
  - `PinnedRow_EditorSurvivesSwitch`.
  - `AllPrompts_ByUse_MostUsedFirst` and `Category_ByName_AToZ`.
  - `PinnedRow_UnpinInMainWindow_UpdatesRow` — `TogglePin` (MainViewModel.cs:2266-2291) today only saves via `SaveTeamState`; it must end with `RefreshUserState()`. Same check for `ToggleHide` (hiding a pinned team prompt drops it from the row).
  - `PromptSortChanged_RaiseUserState_AllPromptsResorted` — `RefreshUserState` (1718) must call `RefreshPrompts()` whenever the search text is empty (not only on Recent), so a sort change or cleared usage re-sorts an open window.
  - Copy from the main window (MainViewModel.cs:1334-1341) also calls `_host.RefreshQuickMenu()` (U8).
- [ ] **Step 2:** `dotnet test tests/PromptLibrary.App.Tests --filter "MainViewModelPinned|CategoryItem"` → FAIL.
- [ ] **Step 3:** Implement following the Recent row's code paths exactly; rail category order stays A–Z (U5). Leave `PromptFilter.Apply` A–Z; re-sort its result in `RefreshPrompts`.
- [ ] **Step 4:** PASS; then full App tests PASS. Commit `feat(main): Pinned rail row and most-used list order`.

---

### Task 5: Settings — Prompt order and Clear usage history

**Files:**
- Create: `src/PromptLibrary.App/Settings/PromptOrderSettings.cs` (testable logic; `SettingsWindow` takes a concrete `AppHost` whose `SaveSettings` writes real %APPDATA%, so the window itself is not unit-tested).
- Modify: `src/PromptLibrary.App/Views/SettingsWindow.xaml` (new section after Appearance), `SettingsWindow.xaml.cs` (wiring only). Must not touch `AppHost.cs` (Task 2 owns it).
- Test: `tests/PromptLibrary.App.Tests/PromptOrderSettingsTests.cs`.

**Interfaces — Produces:** `sealed class PromptOrderSettings(AppSettings settings, Action save, Action refreshTray, Action raiseUserState, Func<bool> confirmClear)` with `bool SortsByUse`, `void SetSortByUse(bool byUse)`, `bool CanClear` (`Usage.Count > 0`), `bool ClearUsage()` (returns true when cleared). Consumes `UsageOrder.ByUse/ByName`.

- [ ] **Step 1: Failing tests** (plain fakes, no UI): `Default_SortsByUse`; `SetSortByUse_False_SetsName_SavesRefreshesRaises` (each action called once); `CanClear_False_WhenNoUsage`; `ClearUsage_Confirmed_EmptiesUsage_SavesRefreshesRaises`; `ClearUsage_Cancelled_KeepsUsage_NoSave`.
- [ ] **Step 2:** `dotnet test tests/PromptLibrary.App.Tests --filter PromptOrderSettings` → FAIL.
- [ ] **Step 3:** Implement the class. Window wiring: build it with `host.Settings`, `() => host.SaveSettings()`, `host.RefreshQuickMenu`, `host.RaiseUserStateChanged`, `() => ConfirmDialog.Ask(this, "Clear usage history?\n\nPrompts go back to A to Z order until you use them again, and Recent is emptied.", "Clear", "Cancel")` (check `ConfirmDialog.Ask`'s real signature; the window title stays "Prompt Library"). XAML: `FieldLabel` "Prompt order", radios `SortUsedRadio` "Most used first" / `SortNameRadio` "A to Z" (copy Theme radio markup + AutomationProperties), muted line, button `ClearUsageButton` "Clear usage history…", status `ClearUsageStatus` (LiveSetting Polite) showing "Usage history cleared.". Subscribe to `host.UserStateChanged` (unsubscribe on close) to refresh `ClearUsageButton.IsEnabled`.
- [ ] **Step 4:** PASS. Commit `feat(settings): prompt order choice and clear usage history`.

---

### Task 6: Release notes, version, gallery (Sonnet)

**Files:** `CHANGELOG.md`, `README.md` (feature list), `src/PromptLibrary.App/PromptLibrary.App.csproj` `<Version>1.4.0</Version>` (and any installer version file that 1.3.0 bumped — `git show d6ba588 --stat` lists them), `tests/PromptLibrary.App.Tests/Gallery/ScreenshotGallery.cs` (add renders: main window with Pinned row selected, Settings scrolled to Prompt order; personal screens use the `NN-` series, next free is `32-`).

- [ ] Steps: edit; `dotnet build` passes; gallery tests compile. Commit `chore: 1.4.0 version, changelog and README`.

---

### Task 7: Branch review and UI critic (Opus, independent)

- Reviewer agent (Opus): diff `main...feature/most-used-order` against the spec only; correctness, the Review Focus list, U5 unchanged lists.
- UI critic (Opus): gallery renders of the Pinned row and Settings section vs `design-quality-bar` rubric; pass at ≥ 90%.
- Fixes go to a Sonnet implementer, max two rounds.

### Task 8: Full verification (Haiku test-runner)

- `dotnet test` (both projects) on the merged branch; report failing names + messages verbatim. Orchestrator then runs it once more itself before claiming done.

# Update Checker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Check GitHub Releases for a newer Prompt Library, tell the user once, keep a quiet reminder, and
install a verified MSI after a clean exit.

**Architecture:** Pure logic goes in `PromptLibrary.Core/Updates`, unit-tested with a fake
`HttpMessageHandler` and clock: version parsing, due rules, state file, GitHub call and downloader. A new
`UpdateCoordinator` in the App owns the timer, balloon, dialog, rail line and tray item. `AppHost` creates it
and passes it the `IUpdateCheckService`. There is no DI container, which matches how `BackupService` is
wired today.

**Tech Stack:** .NET 10, WPF + WinForms tray, System.Text.Json, HttpClient, xUnit 2.9.

**Spec:** `docs/superpowers/specs/2026-11-05-update-checker-design.md`

## Global Constraints

- Endpoint: `GET https://api.github.com/repos/{owner}/{repo}/releases/latest`. Headers:
  `User-Agent: PromptLibrary/{version} (+https://github.com/{owner}/{repo})`,
  `Accept: application/vnd.github+json`, `X-GitHub-Api-Version: 2022-11-28`. No token.
- Repo comes from csproj `<UpdateRepositoryOwner>darretking</UpdateRepositoryOwner>` and
  `<UpdateRepositoryName>prompt-library</UpdateRepositoryName>`, emitted as `AssemblyMetadata`. If either is
  missing, the checker is off.
- An automatic check is due when `lastCheckedAt` is null or ≥ 24 h ago **and** `lastAttemptAt` is null or
  ≥ 1 h ago. A future timestamp counts as due. The first check runs 30 s after start, then the rule is
  re-evaluated every hour. Nothing runs while `FirstRunDone` is false.
- State lives in `%APPDATA%\PromptLibrary\update-check.json`. It is written with `AtomicFile`. A missing or
  unreadable file means empty state.
- Check timeout: 15 s per request. Download: a 60 s stall watchdog plus Cancel. The single `HttpClient` has
  `Timeout = Infinite`.
- Run the MSI only if its size **and** SHA-256 match the release `digest`. If there is no digest, no MSI or
  any failure, show "Couldn't download the update, so the release page will open instead." and open
  `html_url`.
- Copy (exact): balloon "Prompt Library {V} is available. Click to update."; dialog title "Update available";
  body "Prompt Library {V} is available. You have {C}."; link "What's new"; buttons "Update now" / "Not now";
  rail "Update available: v{V}"; tray item "Update to {V}…"; Help item "Check for _updates…"; up to date
  "You're up to date. Prompt Library {C} is the latest version."; failed "Couldn't check for updates. Check
  your internet connection and try again."; rate limited "GitHub is busy right now. Try again in a little
  while."; progress "Downloading update… {n}%"; checking "Checking for updates…".
- Live testing only with `PROMPTLIBRARY_DEBUG_NO_CLOUDS=1` and with the owner's AppData backed up and
  restored. `PROMPTLIBRARY_DEBUG_NO_INSTALL=1` makes Update now stop after verifying the download and show
  the file in Explorer.

## Review Focus

1. Offline at startup and on every hourly tick: no balloon, no dialog, no crash. `lastAttemptAt` back-off
   holds. Test: `CheckAsync_Offline_ReturnsFailedAndStampsAttemptOnly` (Task 3) plus the due test for a
   59-minute attempt (Task 2).
2. The user updated (running version ≥ saved `latestRelease`): the reminder disappears at startup with no
   network. Test: `Reconcile_RunningVersionCaughtUp_ClearsReleaseAndFlags` (Task 2).
3. A tag the app can't parse (`latest`, `release-2026`): "no update", never a crash. Test:
   `CheckAsync_UnparseableTag_ReturnsBadResponse` (Task 3).
4. A truncated or tampered download is never run. Tests: `Download_SizeMismatch_FailsAndDeletesFile` and
   `Download_DigestMismatch_FailsAndDeletesFile` (Task 4).
5. Not now, then a restart: no second balloon or dialog for the same version, but the reminder is still there.
   Test: `ShouldPrompt_DeclinedOrPromptedVersion_IsFalse_NewerVersion_IsTrue` (Task 2).

---

### Task 1: `AppVersion`

**Files:**
- Create: `src/PromptLibrary.Core/Updates/AppVersion.cs`
- Test: `tests/PromptLibrary.Core.Tests/Updates/AppVersionTests.cs`

**Interfaces:**
- Produces: `public readonly record struct AppVersion(int Major, int Minor, int Patch, int Revision, string? PreRelease) : IComparable<AppVersion>`,
  `static bool TryParse(string? text, out AppVersion version)`, `static AppVersion FromSystemVersion(Version v)`,
  comparison operators, and `ToString()` → `"1.2.3"` (adds `.4` only when Revision ≠ 0, and `-pre` when set).

- [ ] **Step 1: Failing tests.**
  `TryParse` gives true for `v1.2.3`, `V1.2.3`, `1.2.3`, `1.2` (Patch 0), `1.2.3.4`, `1.2.3+build.5`
  (metadata dropped), `1.2.3-beta.1` (PreRelease "beta.1"). It gives false for `null`, `""`, `latest`,
  `1`, `1.x.3`, `release-2026`.
  Ordering: `1.0.1 > 1.0.0`, `1.10.0 > 1.9.9`, `2.0 > 1.99.99`, `1.0.1-beta.1 < 1.0.1`,
  `1.0.1.0 == 1.0.1`, `1.0.1.1 > 1.0.1`. `ToString` of `1.0.1.0` is `"1.0.1"`.
- [ ] **Step 2:** `dotnet test tests/PromptLibrary.Core.Tests --filter AppVersionTests` → fails to compile.
- [ ] **Step 3: Implement.** Pre-release order: a version with a pre-release sorts below the same version
  without one. Two pre-releases compare ordinal by string.
- [ ] **Step 4:** Same filter → PASS.
- [ ] **Step 5:** Commit `feat(updates): semantic version parsing and ordering`.

### Task 2: State, schedule rules, store

**Files:**
- Create: `src/PromptLibrary.Core/Updates/ReleaseInfo.cs`, `UpdateState.cs`, `UpdateSchedule.cs`, `UpdateStore.cs`
- Test: `tests/PromptLibrary.Core.Tests/Updates/UpdateScheduleTests.cs`, `UpdateStoreTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record ReleaseInfo(string Version, string Tag, string PageUrl, string? AssetName, string? AssetUrl, long? AssetSize, string? AssetSha256)`.
    `Version` is the parsed version's `ToString()`. `AssetSha256` is lowercase hex without `sha256:`.
  - `public sealed class UpdateState { DateTimeOffset? LastCheckedAt; DateTimeOffset? LastAttemptAt; ReleaseInfo? LatestRelease; string? DeclinedVersion; string? PromptedVersion; }`
    (auto-properties).
  - `public static class UpdateSchedule`:
    `static readonly TimeSpan CheckInterval = 24h, RetryInterval = 1h;`
    `static bool IsDue(UpdateState s, DateTimeOffset now)`;
    `static bool HasNewer(UpdateState s, AppVersion running)` (the saved latest is parseable and > running);
    `static bool ShouldPrompt(UpdateState s, AppVersion running)` (HasNewer, and the latest version is
    neither `DeclinedVersion` nor `PromptedVersion`);
    `static bool Reconcile(UpdateState s, AppVersion running)` (if the latest is null, unparseable or
    ≤ running, clear LatestRelease, DeclinedVersion and PromptedVersion; return true when anything changed).
  - `public static class UpdateStore { static UpdateState Load(string path); static void Save(string path, UpdateState state); }`.
    Load never throws (missing or unreadable → `new UpdateState()`). Save uses `AtomicFile.WriteAllText`,
    camelCase and indented, the same options as `JsonStore`. Save throws IO errors, and the caller catches them.

- [ ] **Step 1: Failing tests.**
  `IsDue`: an empty state is due; checked 23 h ago is not; checked 25 h ago is due; checked 25 h ago with an
  attempt 59 min ago is not; with an attempt 61 min ago it is due; checked 2 h in the future is due.
  `ShouldPrompt_DeclinedOrPromptedVersion_IsFalse_NewerVersion_IsTrue`: latest 1.0.2 and running 1.0.1 →
  true; declined "1.0.2" → false; prompted "1.0.2" → false; declined "1.0.2" with latest 1.0.3 → true.
  `Reconcile_RunningVersionCaughtUp_ClearsReleaseAndFlags`: latest 1.0.2, declined 1.0.2, running 1.0.2 →
  all three null, returns true. Running 1.0.1 → unchanged, returns false.
  `UpdateStoreTests`: round-trip every field; a missing file → empty state; `"{not json"` → empty state.
- [ ] **Step 2:** Run the filter `Updates` → compile fail.
- [ ] **Step 3:** Implement as specified.
- [ ] **Step 4:** Filter `Updates` → PASS.
- [ ] **Step 5:** Commit `feat(updates): update state, schedule rules and store`.

### Task 3: `UpdateCheckService`

**Files:**
- Create: `src/PromptLibrary.Core/Updates/IUpdateCheckService.cs`, `UpdateCheckService.cs`, `UpdateCheckResult.cs`, `UpdateOptions.cs`
- Test: `tests/PromptLibrary.Core.Tests/Updates/UpdateCheckServiceTests.cs` (+ `FakeHandler` in the same folder)

**Interfaces:**
- Consumes: Task 1 and 2 types.
- Produces:
  - `public sealed record UpdateOptions(string Owner, string Repo, AppVersion Running, string StatePath, string AssetName = "PromptLibrary-Setup.msi")`.
  - `public enum UpdateCheckStatus { UpToDate, UpdateAvailable, NotDue, Failed }`;
    `public enum UpdateFailure { None, Network, Timeout, RateLimited, NotFound, BadResponse }`;
    `public sealed record UpdateCheckResult(UpdateCheckStatus Status, ReleaseInfo? Release, UpdateFailure Failure = UpdateFailure.None)`.
  - `public interface IUpdateCheckService { UpdateState State { get; } Task<UpdateCheckResult> CheckAsync(bool manual, CancellationToken ct = default); void MarkPrompted(string version); void MarkDeclined(string version); }`
  - `public sealed class UpdateCheckService(HttpClient http, UpdateOptions options, Func<DateTimeOffset> now) : IUpdateCheckService`.
    The constructor loads state and runs `Reconcile`, saving if it changed. Concurrent calls share one
    in-flight `Task`. All state writes are best-effort: IO exceptions are swallowed.

- [ ] **Step 1: Failing tests**, using `FakeHandler` (records the request and returns a canned response or
  throws). The JSON fixture is a real-shaped `releases/latest` body with `tag_name`, `html_url` and an
  `assets[]` holding `name`, `browser_download_url`, `size` and `digest: "sha256:<64 hex>"`.
  - `CheckAsync_Newer_ReturnsUpdateAvailableAndSavesRelease` (running 1.0.0, tag `v1.0.1`).
  - `CheckAsync_SameOrOlder_ReturnsUpToDate` (tags `v1.0.0` and `0.9.0`).
  - `CheckAsync_SendsUserAgentAcceptAndApiVersion`: URL
    `https://api.github.com/repos/darretking/prompt-library/releases/latest`, the User-Agent starts with
    `PromptLibrary/1.0.0`, and the other two headers match exactly.
  - `CheckAsync_403And429_ReturnRateLimited`, `_404_ReturnsNotFound`, `_500_ReturnsBadResponse`.
  - `CheckAsync_Offline_ReturnsFailedAndStampsAttemptOnly` (the handler throws `HttpRequestException`;
    `LastAttemptAt` is set and `LastCheckedAt` stays null).
  - `CheckAsync_Timeout_ReturnsTimeout` (the handler delays past a 50 ms test timeout passed through an
    internal `CheckTimeout` property).
  - `CheckAsync_BadJson_ReturnsBadResponse`; `CheckAsync_UnparseableTag_ReturnsBadResponse`.
  - `CheckAsync_NoMsiAsset_StillReportsUpdateWithNullAsset`; `CheckAsync_FallsBackToFirstMsiAsset`;
    `CheckAsync_MissingDigest_LeavesSha256Null`.
  - `CheckAsync_AutomaticWithinWindow_ReturnsNotDueWithoutRequest`; `CheckAsync_Manual_IgnoresWindow`.
  - `MarkDeclined_And_MarkPrompted_Persist` (a new service on the same path sees them).
- [ ] **Step 2:** Filter `UpdateCheckServiceTests` → fail.
- [ ] **Step 3:** Implement. Parse with `JsonDocument`, not POCOs, so unknown or missing fields are tolerated.
  Only the absence of `tag_name` or `html_url` gives BadResponse.
- [ ] **Step 4:** Filter → PASS.
- [ ] **Step 5:** Commit `feat(updates): GitHub latest-release check with 24h rule`.

### Task 4: `UpdateDownloader`

**Files:**
- Create: `src/PromptLibrary.Core/Updates/UpdateDownloader.cs`
- Test: `tests/PromptLibrary.Core.Tests/Updates/UpdateDownloaderTests.cs`

**Interfaces:**
- Produces: `public sealed class UpdateDownloader(HttpClient http)`,
  `Task<string> DownloadAsync(ReleaseInfo release, string folder, IProgress<double>? progress, CancellationToken ct)`.
  It returns the verified file path `folder\PromptLibrary-Setup-{Version}.msi`. It throws
  `UpdateDownloadException(string message)` on: no asset, no digest, size mismatch, digest mismatch,
  60 s stall (internal `StallTimeout` for tests) or HTTP error. On any failure it deletes the partial file.
  `OperationCanceledException` passes through for a user Cancel, and the file is deleted then too.

- [ ] **Step 1: Failing tests:** `Download_Good_ReturnsPathAndReportsProgressTo1`,
  `Download_SizeMismatch_FailsAndDeletesFile`, `Download_DigestMismatch_FailsAndDeletesFile`,
  `Download_NoDigest_FailsWithoutRequest`, `Download_Cancel_ThrowsOperationCanceledAndDeletes`,
  `Download_Stall_Fails` (a stream that blocks while `StallTimeout` is 100 ms),
  `Download_ReplacesExistingFile`.
- [ ] **Step 2:** Run → fail.
- [ ] **Step 3:** Implement. Stream into the file in 80 KB chunks while hashing with `IncrementalHash`.
  Each read uses a linked CTS with `CancelAfter(StallTimeout)`, reset on every chunk.
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(updates): verified installer download`.

### Task 5: App wiring: dialog, coordinator, rail, tray, Help, exit-then-install

**Files:**
- Create: `src/PromptLibrary.App/Updates/UpdateCoordinator.cs`, `src/PromptLibrary.App/Views/UpdateDialog.xaml(.cs)`
- Modify: `PromptLibrary.App.csproj` (repo properties → `AssemblyMetadata`), `Paths.cs` (`UpdateStateFile`,
  `UpdateDownloadFolder = %TEMP%\PromptLibrary`), `Tray/TrayIcon.cs`, `AppHost.cs`,
  `ViewModels/MainViewModel.cs`, `Views/MainWindow.xaml`

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces:
  - `TrayIcon`: `void ShowMessage(string title, string text, bool isError = false, Action? onClick = null)`.
    The click action is kept until the next balloon or until `BalloonTipClosed`. Also
    `void SetUpdateItem(string? text)`, which adds or removes the top item and separator and survives
    `Rebuild`, and the `event EventHandler? UpdateRequested`.
  - `AppHost.ExitApp(ExitReason reason = ExitReason.User, Action? afterExit = null)` now returns `bool`
    (false when it didn't proceed). It runs `afterExit` (inside try/catch, with failures sent to
    `CrashLog`) right before `Application.Shutdown()`. `async void` becomes `async Task<bool>`. Existing
    callers discard the result with `_ =`, and the result is set when `_exiting` flips.
  - `UpdateCoordinator(AppHost host, IUpdateCheckService service, UpdateDownloader downloader)`:
    `void Start()` (30 s first timer, then hourly, skipped while `!FirstRunDone`);
    `Task CheckNowAsync()` (Help item); `void OpenDialog()`; `void OnMainWindowShown()` (pays the owed
    dialog); `string? ReminderText` and `event EventHandler? ReminderChanged`.
  - `MainViewModel`: `string? UpdateReminder`, `bool ShowUpdateReminder`, `IRelayCommand OpenUpdateCommand`,
    `IAsyncRelayCommand CheckForUpdatesCommand`, and a public `void ShowStatusText(string text)` wrapper over
    `ShowStatus`.
  - `UpdateDialog.Ask(Window? owner, ReleaseInfo release, AppVersion running, Func<IProgress<double>, CancellationToken, Task<bool>> download)`
    → `UpdateChoice { NotNow, Downloaded }`. It shows the ask state, then the progress state with Cancel.
    It follows the `ConfirmDialog` look from App.xaml styles.

- [ ] **Step 1:** csproj: add the two properties and
  `<AssemblyAttribute Include="System.Reflection.AssemblyMetadataAttribute"><_Parameter1>UpdateRepositoryOwner</_Parameter1><_Parameter2>$(UpdateRepositoryOwner)</_Parameter2></AssemblyAttribute>`
  (the same for Name). `dotnet build` → 0 warnings.
- [ ] **Step 2:** TrayIcon balloon-click and update item. Rebuild keeps the update item first.
- [ ] **Step 3:** `ExitApp` → `Task<bool>` with `afterExit`. Run all tests → still 348 passing.
- [ ] **Step 4:** UpdateDialog XAML: title, body, "What's new" link (opens `PageUrl` via
  `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })`), a progress bar, and buttons.
  Esc or close means Not now. During download, Esc means Cancel.
- [ ] **Step 5:** UpdateCoordinator. Automatic result `UpdateAvailable` + `ShouldPrompt` → `MarkPrompted`.
  If the main window is visible, active, not minimized and has no owned windows, show the dialog;
  otherwise show the balloon and set `_dialogOwed = true`. The dialog result NotNow → `MarkDeclined`.
  Downloaded path → `host.ExitApp(User, afterExit: start MSI)`. If that returns false, delete nothing and
  keep the reminder. Download failure → the spec message, then open `PageUrl`. With
  `PROMPTLIBRARY_DEBUG_NO_INSTALL=1`, show the file in Explorer and don't exit. Manual check: "Checking for
  updates…" status, then the dialog / up-to-date / failed / rate-limited messages from Global Constraints.
  Wrap every automatic path in try/catch to `CrashLog`.
- [ ] **Step 6:** AppHost: build `UpdateOptions` from the assembly metadata (null → no coordinator), create the
  shared `HttpClient { Timeout = Infinite }`, start the coordinator after the setup checks in `Start()`, call
  it from `ShowMainWindow`, and from onboarding completion trigger a check 30 s later. Dispose the client in
  `Dispose()`.
- [ ] **Step 7:** MainWindow.xaml: a Help item `Check for _updates…` between Setup guide and About. The rail
  link button sits above `StatusText`, `TextButton` style, `AutomationProperties.Name="Update available"`,
  visible on `ShowUpdateReminder`.
- [ ] **Step 8:** Build 0/0, all tests pass. Commit `feat(updates): update prompt, reminders and install-on-exit`.

### Task 6: Docs, live verification, release hygiene

**Files:**
- Modify: `README.md` (the "Private by default" text and the badge text if needed), `docs/DEVELOPMENT.md`
  (an Updates section: config properties, state file, debug env var, how to test against a real release),
  `docs/manual-test-checklist.md` (an update checks section)

- [ ] **Step 1:** README: "No account. No sign-in. No tracking. Your prompts live on your PC and in the backup
  folder you choose. Once a day the app asks GitHub whether a newer version exists; nothing about you or your
  prompts is sent."
- [ ] **Step 2:** Live run (Debug, `-p:Version=1.0.0`, NO_CLOUDS, NO_INSTALL, owner AppData backed up first):
  Help → Check for updates shows the dialog for 1.0.1. Update now downloads and verifies, then Explorer shows
  the file. Not now shows the rail line and the tray item. A restart brings no balloon, and the reminder is
  still there. Removing `update-check.json` and starting hidden shows the balloon. Offline (block with an
  invalid proxy env var `HTTPS_PROXY=http://127.0.0.1:9`) → a manual check gives the failed message and an
  automatic check stays silent. Screenshot the dialog and the rail line for the UI rubric (≥ 90 %).
- [ ] **Step 3:** Restore AppData, build 0/0, all tests pass, commit `docs: update checker`, push the
  feature branch and fast-forward main.

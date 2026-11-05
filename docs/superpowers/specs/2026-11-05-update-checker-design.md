# Update checker — design

Date: 2026-11-05 · Status: draft for owner approval · App version at time of writing: 1.0.1

## Why

Prompt Library is a tray app that people install once and leave running for weeks. Releases go out on GitHub
(darretking/prompt-library) with a per-user `PromptLibrary-Setup.msi` that already upgrades in place. Today a
user only learns about a new version by visiting GitHub, so most installs would stay on 1.0.x forever. The
1.0.1 release (new starter prompts) is the first case. The update checker tells users when a newer release
exists and gets them onto it in one click, without nagging and without ever getting in the way of the app.

## Goals

- At most once every 24 hours, check GitHub Releases for a newer version, in the background.
- Help → **Check for updates…** checks right away, ignoring the 24-hour limit.
- When a newer version exists, offer **Update now** / **Not now**.
- **Update now** downloads the installer, verifies it, runs it and lets the app close so it can be replaced.
- **Not now** leaves a quiet reminder that stays until the user updates.
- The check never blocks startup, never shows an error by itself, and never crashes the app.

## Non-goals

- Silent or unattended installs. The installer always runs with its normal UI, so the user sees it.
- Pre-releases, drafts, release channels or rollbacks. Only GitHub's "latest" release counts.
- Delta updates, a custom updater exe, or code signing (the MSI is not signed yet; SmartScreen still warns).
- Release notes rendered inside the app. A "What's new" link opens the release page.
- **Owner decision:** no "check automatically" setting and no "Skip this version" button. Only Update now / Not now.

## Anti-goals (it would be a failure even if it worked)

- A dialog popping over the user's work at Windows sign-in. **Owner decision:** the first notice is a tray balloon.
- Any message, sound or balloon when an automatic check fails (offline, rate-limited, GitHub down).
- Asking again about the same version after the user said Not now. The reminder stays; the prompt does not come back.
- Losing unsaved edits because the installer closed the app.
- Sending anything about the user to GitHub beyond what any HTTPS request carries (no IDs, no usage data).

## Owner decisions (2026-11-05)

| Question | Decision |
|---|---|
| Where the "Update available" reminder lives | A clickable line in the main window's category rail (above the existing status text) **and** an item at the top of the tray/shortcut menu. No new status bar. |
| What Update now does | Download the MSI, verify it, run it, and exit the app. If anything fails, open the release page instead. |
| First notice from an automatic check | A tray balloon. Clicking it opens the dialog. |
| Extra controls | None (no on/off setting, no Skip this version). |

## How it works

### 1. Where the code goes

- **Core** (`src/PromptLibrary.Core/Updates/`, no WPF, fully unit-tested):
  - `AppVersion`: parses `v1.2.3`, `1.2.3`, `1.2`, `1.2.3.4`, and ignores a leading `v`/`V` and build metadata
    (`+…`). A pre-release suffix (`-beta.1`) makes it lower than the same version without one. Unparseable
    tags are "no update". Compares numerically by major, minor, patch (a 4th part counts only if non-zero).
  - `ReleaseInfo`: version, tag, release page URL, installer asset (name, download URL, size, SHA-256 or null).
  - `IUpdateCheckService` / `UpdateCheckService`: `CheckAsync(bool manual, CancellationToken)`. Owns the
    24-hour rule, the GitHub call, JSON parsing, version comparison and the state file. Takes an `HttpClient`,
    a clock and the state file path in its constructor so tests can fake all three.
  - `UpdateDownloader`: downloads the asset to a temp file with progress, then checks size and SHA-256.
  - `UpdateState` + load/save through the existing `AtomicFile`/`JsonStore` helpers.
- **App**: the dialog, the rail line, the tray menu item, the Help menu item, the balloon, the timer and the
  "run installer and exit" step live in the existing `AppHost` / `MainViewModel` / `TrayIcon` / views.
  There is no DI container. `AppHost` creates one `UpdateCheckService` and passes the interface to what needs
  it, the same way it already owns `BackupService`.

### 2. Configuration

- The repository is set by MSBuild properties in `PromptLibrary.App.csproj`, `<UpdateRepositoryOwner>darretking</…>`
  and `<UpdateRepositoryName>prompt-library</…>`, emitted as `AssemblyMetadata` and read at startup. A fork
  changes two lines. If either is missing, the checker is off.
- Installer asset name: `PromptLibrary-Setup.msi`. If it's absent, use the first asset ending in `.msi`.
  If there is no MSI at all, Update now opens the release page.
- Current version: the app assembly's version (`GetName().Version`, three parts), the same value About shows.

### 3. The GitHub call

- `GET https://api.github.com/repos/{owner}/{repo}/releases/latest`. This endpoint already excludes drafts
  and pre-releases.
- Headers: `User-Agent: PromptLibrary/{version} (+https://github.com/{owner}/{repo})`,
  `Accept: application/vnd.github+json`, `X-GitHub-Api-Version: 2022-11-28`. No token.
- One shared `HttpClient`, with a 15-second timeout for the check.
- Fields used: `tag_name`, `html_url`, `assets[].name`, `.browser_download_url`, `.size`, `.digest`
  (`sha256:<hex>`; may be missing on older releases).
- Results: `UpToDate`, `UpdateAvailable(ReleaseInfo)`, `NotDue` (automatic check within 24 h), or
  `Failed(kind)`. The kinds are Offline/Network, RateLimited (403 or 429), NotFound (404 or no releases),
  BadResponse (other status codes, bad JSON, unparseable tag) and Timeout. The service never throws for
  these. It catches `HttpRequestException`, `TaskCanceledException` (timeout), `JsonException`, `IOException`
  and returns `Failed`. A programming bug can still throw; the App layer catches everything around the
  automatic check so it can't crash the app.

### 4. The 24-hour rule and saved state

The state file is `%APPDATA%\PromptLibrary\update-check.json`, separate from `settings.json` so background
writes never race the settings saves:

| Field | Meaning |
|---|---|
| `lastCheckedAt` | Last **successful** check (any result except Failed). |
| `lastAttemptAt` | Last attempt, successful or not. |
| `latestRelease` | The newest `ReleaseInfo` seen, or null. |
| `declinedVersion` | The version the user said Not now to, or null. |
| `promptedVersion` | The version the user has already been shown a balloon or dialog for, or null. |

- An automatic check is due when `lastCheckedAt` is null or ≥ 24 h ago **and** `lastAttemptAt` is null or
  ≥ 1 h ago. The second rule stops an offline laptop from retrying every minute. A clock moved backwards
  (a time in the future) counts as due.
- The app checks about 30 s after startup, then a timer re-evaluates "due?" every hour, because the tray app
  can run for days. No check runs before setup is finished (`FirstRunDone` false). The first check runs about
  30 s after the setup guide closes.
- A manual check always runs and updates both times. On failure it updates `lastAttemptAt` only.
- On startup, before any network call: if `latestRelease` is newer than the running version, the reminder
  shows right away from the saved state. If the running version is now ≥ `latestRelease` (the user updated),
  `latestRelease`, `declinedVersion` and `promptedVersion` are cleared.
- If the file is missing or unreadable, start from empty state (never an error).

### 5. What the user sees

**Automatic check finds a newer version V** (not declined, not yet prompted):

- Tray balloon: "Prompt Library V is available. Click to update." Set `promptedVersion = V`.
- Clicking the balloon opens the update dialog. If the main window is open and in front when the check
  finishes, show the dialog there directly instead of the balloon. Otherwise the dialog shows once, the next
  time the user opens the main window, if they haven't already acted on the balloon.
- The rail line and the tray menu item appear at once (see Not now). They don't wait for a decision.

**The update dialog** follows the app's existing dialog style (`ConfirmDialog` look, `docs/design/ui-style.md`):

- Title: "Update available". Text: "Prompt Library V is available. You have C." Then a "What's new" link to the
  release page.
- Buttons: **Update now** (primary) and **Not now**. Esc and the close button mean Not now.

**Not now:** set `declinedVersion = V`. No more balloons or automatic dialogs for V. The reminder stays:

- Rail line: "Update available: vV" as a link-style button, placed above `StatusText` at the bottom of the
  category rail. It is visible whenever `latestRelease` is newer than the running version. Clicking it opens
  the dialog.
- Tray/shortcut menu: "Update to V…" as the first item, followed by a separator. Clicking it opens the dialog
  (and the main window is not required).
- A newer release W > V later prompts again (balloon and dialog) once, because W ≠ `declinedVersion`.

**Update now:**

1. The dialog switches to "Downloading update… 42%" with a progress bar and **Cancel**.
2. Download to `%TEMP%\PromptLibrary\PromptLibrary-Setup-V.msi`, replacing any old file there. Check the size
   matches. If a digest exists, check the SHA-256 matches. On mismatch, delete the file.
3. Before running it: if there are unsaved edits, ask the existing "Save changes?" question. Cancel there
   stops the update and leaves the dialog closed with the reminder still showing.
4. Start the MSI (shell-execute the `.msi` file, normal installer UI), then call `ExitApp(ExitReason.User)`
   so the usual save, close and backup-on-exit runs. The installer's own close-running-app step stays as a
   safety net. The installer's Finish page relaunches the app.
5. Any failure in steps 1–4 (download error, timeout, mismatch, no MSI asset, installer won't start): message
   "Couldn't download the update, so the release page will open instead." Then open the release page in the
   default browser. The app keeps running. The download has no overall time limit (the user can Cancel), but it
   fails if no data arrives for 60 s.

**Help → Check for updates…** (new item between Setup guide and About):

- While checking, the rail status reads "Checking for updates…".
- Newer version found: the dialog shows, even if that version was declined before.
- Up to date: message "You're up to date. Prompt Library C is the latest version."
- Failed: message "Couldn't check for updates. Check your internet connection and try again." For
  RateLimited: "GitHub is busy right now. Try again in a little while."
- A manual check while one is already running joins that check; it doesn't start a second one.

### 6. Text and privacy

- The README "Private by default" text says "No servers". Change it to say the app's only network use is a
  daily check of GitHub for a newer version, which sends nothing about you or your prompts. Add the same note
  in `docs/DEVELOPMENT.md`.
- No telemetry, no extra headers, no IDs.

### 7. Testing

- **Core unit tests:** version parsing and ordering (prefixes, 2/3/4 parts, pre-release, junk); the due rules
  (null, 23 h/25 h, 1 h attempt back-off, a future timestamp); state round-trip and an unreadable file;
  `CheckAsync` with a fake `HttpMessageHandler` (200 newer, 200 same, 200 older, 403, 429, 404, offline
  exception, timeout, bad JSON, missing assets, missing digest); the request's URL and headers (User-Agent
  present); the declined/prompted bookkeeping; the downloader (good file, size mismatch, digest mismatch,
  cancel).
- **App:** existing test seam only. The dialog, rail line, tray item and balloon are checked by running the
  app. For a live run, build with `-p:Version=1.0.0` so the real v1.0.1 release counts as newer. Follow the
  existing rule: run only with `PROMPTLIBRARY_DEBUG_NO_CLOUDS=1` or the stand-in demo folder, with the owner's
  AppData backed up and restored. A debug-only env var `PROMPTLIBRARY_DEBUG_NO_INSTALL=1` makes Update now
  stop after the download and verify (it shows the file in Explorer instead of running it), so a test never
  installs over the owner's copy.

## Calls made after the builder check (Claude's calls, not owner decisions)

1. **Missing digest.** Run the installer only if both the size and the SHA-256 match. If a release has no
   `digest` for the MSI, Update now opens the release page instead. Every release since 1.0.0 has a digest,
   so this is the rare path. The app never runs a download it couldn't verify.
2. **Order of exit and install.** Update now asks the existing "Save changes?" question first. Cancel stops
   there: nothing is launched and the reminder stays. Otherwise the normal exit runs to the end (windows
   close, library written, exit backup up to 10 s), and the MSI is started as the very last step, just before
   `Application.Shutdown()`. So the installer never closes the app mid-save or mid-backup. `ExitApp` gains an
   optional "after exit" action and reports whether the exit went ahead. An open close question still blocks
   it, as today.
3. **Balloon.** `TrayIcon` gets a balloon-click event tied to the release it announced. `promptedVersion` is set
   when the balloon is shown, because Windows can't tell us whether Focus Assist hid it. A separate in-memory
   "dialog still owed" flag makes the dialog appear the next time the main window is shown, until it has been
   shown once or answered.
4. **"Main window in front".** This means visible, not minimized, active, and with no dialog of its own open.
   Otherwise the balloon is used. The dialog is never stacked over another open dialog. It waits for that
   dialog to close.
5. **Version parts.** Compare major.minor.patch. A 4th part counts only if it isn't 0. Release builds set
   `<Version>` to three parts, so the exe is x.y.z.0.
6. **Manual checks** never return `NotDue`.
7. **HTTP.** One `HttpClient` with no client-wide timeout (`Timeout = Infinite`). The check uses a 15 s
   per-request cancellation; the download uses a 60 s stall watchdog plus the user's Cancel.

## Left to the builder

- Exact XAML for the dialog, its progress state and the rail line, within `ui-style.md`. It must pass the
  existing UI rubric (≥ 90 %).
- Splitting work out of `AppHost` (already ~1,180 lines). Putting the update wiring in a small
  `UpdateCoordinator` class is encouraged.
- Exact wording beyond the strings above, as long as it matches the app's plain, short tone.

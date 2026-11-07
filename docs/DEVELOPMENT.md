# Developing Prompt Library

Everything you need to build, test and package the app. For what the app does, see the [README](../README.md).

## Tech stack

| Area | What's used |
| --- | --- |
| Language & runtime | C# on [.NET 10](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/overview) |
| Windows | [WPF](https://learn.microsoft.com/dotnet/desktop/wpf/); WinForms `NotifyIcon` for the tray and native quick menu |
| MVVM | [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) |
| Win32 | `RegisterHotKey`, `SendInput` (auto-paste), a low-level keyboard hook only while a shortcut is being chosen |
| Storage | Human-readable JSON via `System.Text.Json`, written atomically and flushed to disk, previous save kept as `.bak` |
| Tests | [xUnit](https://xunit.net/): Core library tests plus STA tests with real WPF windows |
| Packaging | Single-file self-contained exe (win-x64); per-user MSI built with [WiX Toolset v5](https://wixtoolset.org/) from NuGet |

## Project layout

```
src/PromptLibrary.Core/     Models, storage, editing rules, search, backups, cloud detection, paste rules (no UI)
src/PromptLibrary.App/      Tray, hotkey, windows, view models, Win32 interop
tests/PromptLibrary.Core.Tests/
tests/PromptLibrary.App.Tests/
installer/                  WiX project (not in the .sln, so build and test stay fast)
build/                      Installer build script and the installer art generator
docs/                       Design spec, plan, style guide, review results, test checklists
```

## Prerequisites

- Windows 10 or 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Python with Pillow, only to redraw the installer art

## Build, run, test

```bash
git clone https://github.com/darretking/prompt-library.git
cd prompt-library
dotnet build
dotnet run --project src/PromptLibrary.App
dotnet test
```

Debug builds never add themselves to Windows startup; Release builds do (when the setting is on).

### Development switch

`PROMPTLIBRARY_DEBUG_NO_CLOUDS=1` (Debug builds only) hides detected cloud folders, so testing never touches your real OneDrive, Google Drive, Dropbox or iCloud.

`PROMPTLIBRARY_DEBUG_NO_INSTALL=1` makes the update dialog's **Update now** stop after downloading and verifying the installer and show it in Explorer, so testing never installs over your copy.

### Update checks

The app asks GitHub for the latest release (`GET /repos/{owner}/{repo}/releases/latest`, no token, a `PromptLibrary/<version>` User-Agent) about 30 s after start and then re-checks hourly whether a check is due: at most one answered check per 24 hours, and at most one attempt per hour while offline. Nothing is checked before setup is finished. **Help → Check for updates…** always checks.

- **Repository:** `<UpdateRepositoryOwner>` and `<UpdateRepositoryName>` in `src/PromptLibrary.App/PromptLibrary.App.csproj` (baked into the exe as assembly metadata). A fork changes these; leaving one empty turns the checker off.
- **Versions:** the release tag (`v1.0.2`) is compared with the exe's version (`<Version>`). Pre-releases and drafts are never offered.
- **Flow:** a new version is announced once (tray notification, or the dialog if the main window is in front). **Not now** leaves "Update available" in the category rail and "Update to X…" at the top of the tray menu. **Update now** downloads `PromptLibrary-Setup.msi` to `%TEMP%\PromptLibrary\`, checks its size and SHA-256 against the release's `digest`, exits the app the normal way (save question, exit backup) and then starts the installer. Any failure opens the release page instead.
- **Releases** therefore need the MSI attached as an asset; GitHub adds the `digest` automatically.
- **To test against a real release,** build an older version: `dotnet build src/PromptLibrary.App -p:Version=1.0.0`, then run with both debug switches above and use Help → Check for updates…. Back up `%APPDATA%\PromptLibrary` first and delete `update-check.json` to start fresh.

### Team library

A team shares one file, `team-prompts.json`, in a shared folder (network drive, or a OneDrive / Google Drive / Dropbox synced folder) or at a public https link. Members see it read-only on the **Team prompts** side; anyone whose PC can write to the file can choose **Edit team library**. The code is in `src/PromptLibrary.Core/Team/` (format, reading, writing, pins and markers) and `src/PromptLibrary.App/Team/TeamLibraryService.cs` (refresh, offline copy, joining, saving).

- **File format:** the same shape as `library.json` (`schemaVersion`, `categories`, `prompts` with GUID IDs and `createdAt` / `updatedAt`), plus `"kind": "prompt-library-team"`, a `name` (for example "Acme best practice") and an `updatedAt`. Unknown fields are kept when the app saves, so a later version can add some. A file with a higher `schemaVersion` can still be viewed but not edited.
- **Sources:** one box takes a folder (the app looks for `team-prompts.json` in it), the file itself, or an https link. A link must return the file: GitHub raw links and Dropbox links work (Dropbox `dl=0` is rewritten to `dl=1`, GitHub `/blob/` to raw). A link that returns a web page (OneDrive and Google Drive share pages) is refused with a message saying so. Links are https only, no sign-in, 15 s time limit, 2 MB cap, the normal `PromptLibrary/<version>` User-Agent, and never a redirect to http.
- **Saving (editors):** before each save the app re-reads the file and compares its SHA-256 with the one it loaded. If someone else changed it, the save is refused, the library reloads and the typed change stays in the editor to save again. Otherwise the current file is copied to `Prompt Library history\team-prompts <yyyy-MM-dd_HHmmss>.json` next to it (the newest 30 are kept) and the new file is written atomically. **⋯ → Restore an older version…** lists that history.
- **Sync conflicts:** sibling files named `team-prompts*.json` (OneDrive's `team-prompts-PCNAME.json`, Dropbox's `… (conflicted copy …).json`) make editors see "Two versions of the team library found · Review". Restoring one, or **Keep current version**, moves the copies into the history folder.
- **Write access** is a probe: the file is opened for writing without changing it, at connect, on each refresh and on Retry. Non-writers see "Read only".
- **Refresh:** at start (from the offline copy first, then the source), every hour, two seconds after a change to a local or synced file (a file watcher), and on **Retry**. Nothing is read on the UI thread. While an editor has the library unlocked, refreshes wait until **Done**. Bad or unreadable data never replaces the last good copy; the Team header then says "can't reach the team library" with Retry, only after a read has failed.
- **Offline copy:** `%APPDATA%\PromptLibrary\team-cache.json` holds the last good file, when it was read and from where. Personal backups never include team prompts: the shared file is the master and its history is the team's backup.
- **Per PC** (in `settings.json`): `teamSource`, `pinnedPromptIds` (either library, at most 10), `hiddenTeamPromptIds`, `seenTeamPrompts` (what drives the New / Updated tags) and `teamBaselineTaken`. On first join nothing is marked new. **Leave team library** clears all of these and the offline copy; copies made with **Make my copy** stay.
- **Start a team library** writes a new file only into a folder that hasn't got one (otherwise it offers **Join it**). It can copy in some of your categories (with new IDs) and, by default, 24 business example prompts in 6 categories (`TeamSeedLibrary`).

**Testing a team by hand.** Never point a test at a real shared folder. Make two stand-in folders anywhere outside your cloud folders, run the app with `PROMPTLIBRARY_DEBUG_NO_CLOUDS=1`, and **Start a team library** in the first (name it "Acme best practice"). Copy its `team-prompts.json` to the second folder and run `attrib +r team-prompts.json` there to get a read-only member. Edit the file in a text editor while the app is in edit mode to try a refused save, copy it to `team-prompts-LAPTOP.json` to try a sync conflict, and rename the folder to try offline. Back up `%APPDATA%\PromptLibrary` first: the app has no switch to use another data folder. The steps are in [manual-test-checklist.md](manual-test-checklist.md#team-prompts).

### Manual checks

Some things only a real desktop can show (auto-paste into other apps, notifications, display scaling). They're listed in [manual-test-checklist.md](manual-test-checklist.md) and [edge-cases.md](edge-cases.md).

## Publish

Single self-contained exe, `publish/PromptLibrary.exe` (about 75 MB):

```bash
dotnet publish src/PromptLibrary.App -c Release -o publish
```

## Installer

```powershell
.\build\build-installer.ps1
```

Publishes the exe, builds `installer/PromptLibrary.Installer.wixproj` and writes `artifacts/PromptLibrary-Setup.msi`. The installer's version comes from the exe: bump `<Version>` in `src/PromptLibrary.App/PromptLibrary.App.csproj` for each release, or pass `-Version 1.0.1` for a one-off build. Redraw the installer art with `python build/make-installer-bitmaps.py`.

The MSI installs per user (no admin prompt) to `%LOCALAPPDATA%\Programs\Prompt Library`, closes a running copy on upgrade or uninstall, and never touches the user's data in `%APPDATA%\PromptLibrary`. It isn't code-signed yet, so SmartScreen shows an "unknown publisher" warning on download.

## Where data lives

`%APPDATA%\PromptLibrary\`

| File | What it is |
| --- | --- |
| `library.json` | Prompts and categories (backed up) |
| `library.json.bak` | The save before the latest one |
| `settings.json` | This PC's settings (not backed up) |
| `Recovered\` | Unsaved edits rescued after an unexpected close |
| `Safety backups\` | Copies made before a restore when backups are off |
| `crash.log` | Details of any unexpected error |
| `update-check.json` | When GitHub was last asked about updates, the newest release seen, and the version you said "Not now" to |
| `team-cache.json` | The last good copy of the team library, for offline use (only while a team library is joined) |

## Design

- Spec: [superpowers/specs/2026-11-04-prompt-library-design.md](superpowers/specs/2026-11-04-prompt-library-design.md)
- Style guide: [design/ui-style.md](design/ui-style.md) · Review rubric: [design/ui-review-rubric.md](design/ui-review-rubric.md) · Results: [design/ui-review-results.md](design/ui-review-results.md)

| Colour | Hex | Used for |
| --- | --- | --- |
| Brand (indigo) | ![#4338CA](https://placehold.co/15x15/4338CA/4338CA.png) `#4338CA` | Copy button, selection, focus ring, links, app icon |
| Brand soft | ![#E8E7FB](https://placehold.co/15x15/E8E7FB/E8E7FB.png) `#E8E7FB` | Selected list item |
| Accent (amber) | ![#F5A524](https://placehold.co/15x15/F5A524/F5A524.png) `#F5A524` | Icon bookmark, setup illustration |
| Ink | ![#1B2230](https://placehold.co/15x15/1B2230/1B2230.png) `#1B2230` | Primary text |
| Window | ![#F6F7F9](https://placehold.co/15x15/F6F7F9/F6F7F9.png) `#F6F7F9` | Window background |

## Roadmap

- [ ] Import prompts from files and other tools
- [ ] Fill-in placeholders (like `{product}`) before pasting
- [ ] Code-signed installer
- [ ] App-level tests for the main view model, then split `AppHost` (backup coordinator) and `MainViewModel` (category rail)

## Contributing

Open an issue first to talk a change through, then send a pull request. Anything in `src/PromptLibrary.Core` needs tests.

## License

[GPL-3.0-or-later](../LICENSE). Copyright © 2026 Darret King. By contributing you agree your changes are released under the same licence.

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

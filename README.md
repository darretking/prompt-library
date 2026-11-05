<!-- Header -->
<div align="center">

  <img src="docs/images/logo.png" alt="Prompt Library logo" width="120" height="120" />
  <h1>Prompt Library</h1>

  <p>
    All your AI prompts in one place. Press a shortcut in any app, pick a prompt, and it's pasted right where you were typing.
  </p>

<!-- Badges -->
<p>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-4338CA" alt="Platform: Windows 10 | 11" />
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10" />
  <img src="https://img.shields.io/badge/UI-WPF-4338CA" alt="WPF" />
  <img src="https://img.shields.io/badge/tests-xUnit-F5A524" alt="Tests: xUnit" />
  <img src="https://img.shields.io/badge/version-1.0.0-4338CA" alt="Version 1.0.0" />
  <img src="https://img.shields.io/badge/license-to%20be%20decided-lightgrey" alt="License: to be decided" />
</p>

<h4>
    <a href="#camera-screenshots">Screenshots</a>
  <span> · </span>
    <a href="docs/manual-test-checklist.md">Test checklist</a>
  <span> · </span>
    <a href="https://github.com/darretking/prompt-library/issues/new?labels=bug">Report a bug</a>
  <span> · </span>
    <a href="https://github.com/darretking/prompt-library/issues/new?labels=enhancement">Request a feature</a>
  </h4>
</div>

<br />

<!-- Table of Contents -->
# :notebook_with_decorative_cover: Table of contents

- [About the project](#star2-about-the-project)
  * [Screenshots](#camera-screenshots)
  * [Tech stack](#space_invader-tech-stack)
  * [Features](#dart-features)
  * [Colour reference](#art-colour-reference)
  * [Environment variables](#key-environment-variables)
- [Getting started](#toolbox-getting-started)
  * [Prerequisites](#bangbang-prerequisites)
  * [Installation](#gear-installation)
  * [Running tests](#test_tube-running-tests)
  * [Run locally](#running-run-locally)
  * [Deployment](#triangular_flag_on_post-deployment)
- [Usage](#eyes-usage)
- [Where your data lives](#floppy_disk-where-your-data-lives)
- [Roadmap](#compass-roadmap)
- [Contributing](#wave-contributing)
- [FAQ](#grey_question-faq)
- [License](#warning-license)
- [Contact](#handshake-contact)
- [Acknowledgements](#gem-acknowledgements)

<!-- About the Project -->
## :star2: About the project

Prompts tend to end up scattered across documents, notes apps and chat histories. Prompt Library is a small Windows tray app that keeps them in one library, organised by category, and gets any of them into the app you're working in (a terminal, a browser, your IDE, Claude Code) with one shortcut.

<!-- Screenshots -->
### :camera: Screenshots

<div align="center">
  <img src="docs/images/main-window.png" alt="Main window: categories, prompts and the prompt editor" />
  <br /><br />
  <img src="docs/images/setup-wizard.png" alt="First-run setup: trying the shortcut" />
</div>

<!-- TechStack -->
### :space_invader: Tech stack

<details>
  <summary>App</summary>
  <ul>
    <li><a href="https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/overview">.NET 10</a> (C#)</li>
    <li><a href="https://learn.microsoft.com/dotnet/desktop/wpf/">WPF</a> for the windows, WinForms <code>NotifyIcon</code> for the tray and native quick menu</li>
    <li><a href="https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/">CommunityToolkit.Mvvm</a></li>
    <li>Win32 interop: <code>RegisterHotKey</code>, <code>SendInput</code>, low-level keyboard hook (only while choosing a shortcut)</li>
  </ul>
</details>

<details>
  <summary>Storage</summary>
  <ul>
    <li>Human-readable JSON (<code>System.Text.Json</code>), written atomically and flushed to disk</li>
    <li>Backups as dated JSON copies in a OneDrive, Google Drive, Dropbox, iCloud Drive or any other folder</li>
  </ul>
</details>

<details>
  <summary>Testing</summary>
  <ul>
    <li><a href="https://xunit.net/">xUnit</a> for the Core library (storage, editing, search, backups, cloud detection, paste rules, onboarding flow, edge cases)</li>
    <li>A <a href="docs/manual-test-checklist.md">manual checklist</a> and <a href="docs/edge-cases.md">edge-case list</a> for things only a real desktop can show</li>
  </ul>
</details>

<details>
  <summary>Packaging</summary>
  <ul>
    <li>Single-file, self-contained <code>PromptLibrary.exe</code> (win-x64)</li>
    <li>Per-user MSI built with <a href="https://wixtoolset.org/">WiX Toolset v5</a> (from NuGet, nothing to install)</li>
  </ul>
</details>

<!-- Features -->
### :dart: Features

- **One shortcut, any app.** `Ctrl+Alt+P` opens a native menu at your mouse: category, then prompt.
- **Auto-paste.** Picking a prompt copies it and pastes it where you were typing. Turn it off to only copy.
- **Simple library.** Title, description, category and prompt text, with search across all of them. Line breaks, tabs, quotes, code blocks and emoji are kept exactly.
- **Backups that set themselves up.** OneDrive (personal and work), Google Drive (any drive letter or language), Dropbox and iCloud Drive are found automatically; one click runs the first backup. Backups run daily and on exit, and any of them can be restored.
- **Safe by design.** Atomic, flushed saves; a safety copy before every restore; unsaved edits are never silently lost, even if the app is closed by an installer, a logoff or a crash.
- **First-run setup** in four steps, and an installer that needs no admin rights.

<!-- Color Reference -->
### :art: Colour reference

| Colour | Hex | Used for |
| --- | --- | --- |
| Brand (indigo) | ![#4338CA](https://placehold.co/15x15/4338CA/4338CA.png) `#4338CA` | Copy button, selection edge, focus ring, links, app icon |
| Brand soft | ![#E8E7FB](https://placehold.co/15x15/E8E7FB/E8E7FB.png) `#E8E7FB` | Selected list item |
| Accent (amber) | ![#F5A524](https://placehold.co/15x15/F5A524/F5A524.png) `#F5A524` | Icon bookmark, onboarding illustration |
| Ink | ![#1B2230](https://placehold.co/15x15/1B2230/1B2230.png) `#1B2230` | Primary text |
| Window | ![#F6F7F9](https://placehold.co/15x15/F6F7F9/F6F7F9.png) `#F6F7F9` | Window background |

The full style guide is in [docs/design/ui-style.md](docs/design/ui-style.md).

<!-- Env Variables -->
### :key: Environment variables

None are needed to use the app. For development only (Debug builds):

`PROMPTLIBRARY_DEBUG_NO_CLOUDS=1`: hides detected cloud folders, so testing never touches your real OneDrive, Google Drive, Dropbox or iCloud.

<!-- Getting Started -->
## :toolbox: Getting started

<!-- Prerequisites -->
### :bangbang: Prerequisites

- Windows 10 or 11 (x64)
- To build: the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- For cloud backups (optional): OneDrive (built into Windows), Google Drive for desktop, Dropbox or iCloud for Windows

<!-- Installation -->
### :gear: Installation

Run `PromptLibrary-Setup.msi`. It installs just for you (no admin prompt) to `%LOCALAPPDATA%\Programs\Prompt Library`, adds **Prompt Library** to the Start menu and to Settings → Apps, and its last page starts the app, whose setup walks you through the shortcut and backups.

The installer isn't code-signed yet, so Windows SmartScreen may show "Windows protected your PC". Choose **More info**, then **Run anyway**.

To update, run a newer installer: a running Prompt Library is closed and replaced, and your prompts and settings are kept.

<!-- Running Tests -->
### :test_tube: Running tests

```bash
dotnet test
```

Things only a real desktop can check (auto-paste, notifications, display scaling) are in [docs/manual-test-checklist.md](docs/manual-test-checklist.md).

<!-- Run Locally -->
### :running: Run locally

Clone the project

```bash
git clone https://github.com/darretking/prompt-library.git
```

Go to the project folder

```bash
cd prompt-library
```

Build

```bash
dotnet build
```

Run

```bash
dotnet run --project src/PromptLibrary.App
```

<!-- Deployment -->
### :triangular_flag_on_post: Deployment

Single self-contained exe (`publish/PromptLibrary.exe`, about 75 MB):

```bash
dotnet publish src/PromptLibrary.App -c Release -o publish
```

Installer (`artifacts/PromptLibrary-Setup.msi`):

```powershell
.\build\build-installer.ps1
```

The installer takes its version from the exe: bump `<Version>` in `src/PromptLibrary.App/PromptLibrary.App.csproj` for each release, or pass `-Version 1.0.1` for a one-off build. Installer page art is drawn by `python build/make-installer-bitmaps.py` (needs Pillow).

<!-- Usage -->
## :eyes: Usage

1. Press **`Ctrl+Alt+P`** in any app, or click the tray icon.
2. Pick a category, then a prompt.
3. It's pasted where you were typing. With auto-paste off, press `Ctrl+V` yourself.

Manage prompts from the tray menu → **Open Prompt Library…**: add, edit, search, move and delete prompts and categories. Change the shortcut, backups, auto-paste and start-with-Windows in **Settings**. Restore a backup from **File → Restore from backup…**.

Apps running as administrator can't be pasted into (a Windows rule); the prompt is still copied, and the app tells you to press `Ctrl+V`.

<!-- Data -->
## :floppy_disk: Where your data lives

`%APPDATA%\PromptLibrary\`

- `library.json`: your prompts and categories (backed up).
- `settings.json`: this PC's settings (not backed up).
- `Recovered\`: unsaved edits rescued after an unexpected close, if any.

Uninstalling (Settings → Apps → Prompt Library → Uninstall) removes the app, its shortcut and its start-with-Windows entry, and **keeps your prompts and backups**.

<!-- Roadmap -->
## :compass: Roadmap

* [x] Prompt library with categories, search and editor
* [x] Global shortcut, native quick menu and auto-paste
* [x] Cloud backups with automatic detection, and restore
* [x] First-run setup and per-user installer
* [ ] Import existing prompts from files and other tools
* [ ] Fill-in placeholders (like `{product}`) before pasting
* [ ] Code-signed installer
* [ ] App-level automated tests and a tidier `AppHost` (backup coordinator split)

<!-- Contributing -->
## :wave: Contributing

Ideas and bug reports are welcome through [issues](https://github.com/darretking/prompt-library/issues). For code changes, open an issue first to talk it through, then send a pull request with tests for anything in `src/PromptLibrary.Core`.

<!-- FAQ -->
## :grey_question: FAQ

- **Can I right-click in another app and get my prompts?**

  + Windows doesn't let one app add items to other apps' right-click menus, so Prompt Library uses a shortcut and the tray icon instead. They work the same in every app.

- **Why didn't it paste into my app?**

  + Apps running as administrator block pasted input from normal apps. The prompt is on your clipboard; press `Ctrl+V`. Holding `Ctrl` or `Alt` while picking also copies only.

- **Pasting a long prompt into a terminal ran some lines. Why?**

  + Plain shells treat each line break as Enter. Windows Terminal warns before multi-line pastes; chat boxes and Claude Code handle them fine. You can turn auto-paste off in Settings.

- **What happens if my PC dies?**

  + Turn on backups during setup (or in Settings). Reinstall, open **File → Restore from backup…**, and pick your cloud folder.

<!-- License -->
## :warning: License

To be decided.

<!-- Contact -->
## :handshake: Contact

GitHub: [@darretking](https://github.com/darretking)

Project link: [https://github.com/darretking/prompt-library](https://github.com/darretking/prompt-library)

<!-- Acknowledgments -->
## :gem: Acknowledgements

- [Awesome README template](https://github.com/Louis3797/awesome-readme-template) for this README's layout
- [Shields.io](https://shields.io/) for the badges
- [WiX Toolset](https://wixtoolset.org/) for the installer

# Changelog

All notable changes to Prompt Library are listed here. Download any version from [Releases](https://github.com/darretking/prompt-library/releases).

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [1.2.0] - 2026-11-12

A redesigned, faster Prompt Library with templates, search and history.

### Added
- Prompt templates: `{placeholders}` and dynamic tokens open a fill-in form with a live preview of the result. Choice fields work like a text box with autocomplete.
- Fuzzy, ranked search with match highlights, in a translucent search window with adaptive paste and toast feedback.
- Version history for every prompt: compare any version with the next, and restore an old version as a new prompt.
- Import and export of prompts as JSON or Markdown folders, with a result for each file. A bad file never stops the import.
- Tags, send targets, and usage ranking so your most-used and recent prompts rise to the top.
- Light and dark themes with the Windows 11 Mica backdrop.
- Setting to restore your clipboard after a prompt is pasted.

### Changed
- Redesigned main window: Windows 11 title bar, emoji category rail with Recent, an editor card with tag chips and highlighted prompt text, and a preview of the result.
- Colour emoji everywhere, drawn from bundled Microsoft Fluent Emoji images.
- Setup asks more clearly whether to start Prompt Library with Windows.

### Fixed
- Deleting a category can also delete its prompts, and the dialog no longer contradicts itself.
- Window minimum sizes, per-monitor DPI placement and 200 % scaling.

## [1.1.0] - 2026-11-07

### Added
- Team prompts: join, start or leave a shared team library kept in a cloud folder.
- Edit the team library in place, with history, conflict detection and review of sync copies.
- Pin or hide team prompts, with markers for new and updated ones.
- Team prompts in the shortcut menu, search and the main window, with an offline copy when the team folder can't be reached.

### Fixed
- Settings and the main window fit the work area. The tray menu isn't rebuilt while it's open.
- Team errors show plain text instead of raw failure messages.

## [1.0.2] - 2026-11-05

### Added
- Built-in updates: a once-a-day check for a new version on GitHub, one notification, and **Update now** to download, verify and install it.
- **Help → Check for updates…** to check at any time.

## [1.0.1] - 2026-11-05

### Changed
- New starter set: 39 everyday prompts in 13 categories. Existing libraries are kept when you update.

## [1.0.0] - 2026-11-05

### Added
- First release: a global shortcut (Ctrl + Alt + P) that pastes a prompt into any app, a categorised library with search and an editor, automatic backups to OneDrive, Google Drive, Dropbox, iCloud Drive or any folder, and a short first-run setup.

[1.2.0]: https://github.com/darretking/prompt-library/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/darretking/prompt-library/compare/v1.0.2...v1.1.0
[1.0.2]: https://github.com/darretking/prompt-library/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/darretking/prompt-library/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/darretking/prompt-library/releases/tag/v1.0.0

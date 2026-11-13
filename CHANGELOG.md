# Changelog

All notable changes to Prompt Library are listed here. Download any version from [Releases](https://github.com/darretking/prompt-library/releases).

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.2.1] - 2026-11-13

### Added
- **Settings → Start fresh → Delete all my prompts…** empties Your prompts (every prompt and category), for example to drop the starter prompts. You type DELETE to confirm. Team prompts, and your team pins and hidden team prompts, are not touched. A safety copy is saved first, so Restore from backup brings everything back, and the starter prompts are not added again at the next start.
- **Help → What's new** opens this changelog.
- Markdown export and import keep each category's emoji (`emoji:`, or `icon:`, in the front matter) and the prompt's Send to target (`sendTo:`). An imported emoji is also given to an existing category that has none. JSON export already kept both, and tags were already kept in both formats.
- **Turn off backups** in Settings, and a first line there that says whether backups are on and where they go.
- **File → New prompt** and **New category**.
- The fill-in form says how many fields are blank, and blank fields preview as `{Label}`, as they will be pasted.
- Import results say what to do about each file that couldn't be imported.
- A **Make my copy** link next to a read-only team prompt.
- 18 emoji for business departments and functions in the category emoji picker: people and HR, finance, growth, operations, IT, security, compliance, projects, records, product, logistics, manufacturing, the company, customer support, communications, wins, strategy, and research and testing.

### Changed
- Easier for new users (scored with the rubric in `docs/design/ui-review-rubric.md`, from 82 % to 92 %):
  - The toolbar always says whether edits are kept: "● Unsaved changes" with a **Save** button, or "✓ Saved".
  - **New** (prompt) and **+ New category** are labelled buttons instead of two bare "+".
  - The selected category shows its Rename and Delete buttons.
  - **Send to** names its app ("Send to ChatGPT") and is one framed split button; the search footer says "send to ChatGPT" and "more options".
  - The view switch reads **Text | Preview**; tag boxes always show "Add a tag".
  - Deleting a category asks a plain either/or: move the prompts to a category, or delete them too.
  - Settings: backups come first, each shortcut choice is explained, and "Changes are saved as you make them."
  - The setup guide asks "Paste the prompt for you?" as its own question, and the window is tall enough for it.
  - The app name in the title bar is centred and muted so it never reads as a menu; team prompts get a "Team" badge in search and a "New" tag on the Team prompts switch.
  - Clearer words for import/export choices and errors, and "press Ctrl+V to paste it" when an app couldn't be opened.
- Every row in the category rail shows a colour emoji: All prompts has a house, Recent a clock, and a category without its own emoji a memo. The search window uses the same memo instead of a grey icon.

### Fixed
- In an empty library, choosing New prompt no longer leaves the "Your library is empty" card on top of the new prompt.
- The title bar is one even strip: the menu, the title and the window buttons line up, with no lighter band underneath, restored or maximised.
- The editor card has the same 12 px margin on its left, next to the prompt list, as on its top and right.
- "Move to category" left the ⋯ menu (the category box on the card does it).
- Menus, drop-downs and the emoji picker open from their left edge even when Windows is set to right-handed (they used to open to the left, past the window edge).

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

[Unreleased]: https://github.com/darretking/prompt-library/compare/v1.2.1...HEAD
[1.2.1]: https://github.com/darretking/prompt-library/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/darretking/prompt-library/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/darretking/prompt-library/compare/v1.0.2...v1.1.0
[1.0.2]: https://github.com/darretking/prompt-library/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/darretking/prompt-library/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/darretking/prompt-library/releases/tag/v1.0.0

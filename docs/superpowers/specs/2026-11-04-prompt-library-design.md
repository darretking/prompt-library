# Prompt Library — Design

Date: 2026-11-04
Status: Draft for approval

## 1. Why this exists

The owner keeps AI prompts scattered across folders, documents, programs and online tools. Finding the right one means hunting. They want **one central place** to store prompts and a **fast way to grab any prompt from whatever app they are in** (terminal, browser, IDE, Claude Code, any AI chat box) — plus confidence that the library survives a dead machine.

Success = the owner stops hunting. They press a shortcut (or click the tray icon) anywhere, pick category → prompt, and paste it with Ctrl+V. The library is backed up to Google Drive without them thinking about it.

The owner is a C# developer with no Rust experience. They want to be able to read and maintain the code themselves.

## 2. Goals

1. Store prompts, each with **Title, Description, Category, Prompt text**.
2. Categories are user-defined and editable (add, rename, delete). A prompt's category is chosen from a **dropdown** of existing categories.
3. A **quick menu** that lists categories → prompts; clicking a prompt **copies its text to the clipboard**.
4. Quick menu opens two ways: a **global keyboard shortcut** from any app, and **clicking the tray icon**.
5. **Automatic backup** to Google Drive, with a way to restore.
6. Runs quietly in the system tray; starts with Windows.

## 3. Non-goals (v1)

- **Right-click integration inside other apps.** Windows does not let an app add items to other programs' right-click menus (browsers, terminals, Electron apps draw their own). The shortcut + tray menu replaces this. The owner agreed the quick menu is what matters, not the right-click itself.
- **Auto-pasting** into the target app. Copy only; the user presses Ctrl+V.
- **Importing** existing prompts from other files/tools. Deferred to later.
- Placeholder/variable filling (e.g. prompting for `{product}` before copy). Placeholders are plain text in v1.
- Multi-user, sharing, multi-machine sync, cloud accounts, Google sign-in inside the app.
- Mac/Linux.

## 4. Anti-goals (would make this a failure even if it "works")

- Losing a prompt silently (bad save, crash mid-write, deleting a category that still has prompts, no backup).
- Altering prompt text on copy — whitespace, line breaks, quotes, code fences, braces, emoji must round-trip exactly.
- A slow or clunky quick menu. It must appear instantly and feel like a normal Windows menu.
- A default shortcut that clashes with common tools (e.g. **Ctrl+Shift+P** is VS Code's command palette — do not use it).
- Code the owner can't maintain.

## 5. Decisions

### Decided with the owner
- **Quick menu** opened by global shortcut **and** by tray-icon click. Categories as submenus, prompts inside. Click = copy to clipboard.
- **Copy only**, no auto-paste.
- **Main window layout: three columns** (chosen from mockups): Categories | Prompts in category | Editor.
- **Backup via Google Drive for desktop.** Owner currently uses Drive in the browser only and agreed to install Google Drive for desktop. The app writes backup files into a folder that Drive for desktop syncs; the app contains no Google API code or sign-in.
- **Import of existing prompts: later**, not v1.
- Library size: ~40–60 prompts now. A plain cascading menu is sufficient; no search inside the quick menu.
- Repository: private GitHub repo `darretking/prompt-library`, local at `D:\30_DEV\prompt_library`.

### Claude's calls (owner handed tech choices over; flagged so they can be checked)
- **Stack: C# / WPF on .NET 10** (current LTS). Chosen over Rust (owner's initial idea) and over WinUI 3 because the owner knows C#, WPF is mature for tray + hotkey + desktop apps, and it avoids WinUI packaging friction. The owner accepted the recommendation.
- **Storage:** human-readable **JSON** in `%APPDATA%\PromptLibrary\`, split in two files:
  - `library.json` — categories and prompts only. This is what gets backed up and restored.
  - `settings.json` — hotkey, backup folder, start-with-Windows, backups-to-keep, last-backup time. Machine-specific; **not** backed up or restored, so a restore never rolls back the hotkey or points at a stale backup path, and recording a backup time doesn't count as a library change.
  - Both written atomically (write temp file, then replace) so a crash can't corrupt them.
- **Line endings:** the app normalises prompt text to **CRLF** (Windows standard) when saving and copies it to the clipboard as CRLF. Everything else in the text (spaces, tabs, quotes, braces, emoji, trailing whitespace) is kept exactly. "Round-trip exactly" in section 4 means this.
- **The app owns the files.** Hand-editing `library.json` while the app is running is not supported (the app's next save wins). Editing it while the app is closed is fine. If `library.json` can't be read at startup, the app never overwrites it: it shows an error and offers Restore from backup.
- **One running copy only.** Starting the app again just opens the main window of the copy already running.
- **Default shortcut: Ctrl+Alt+P**, changeable in Settings. If registration fails (taken by another app), show a tray notification and open Settings to pick another.
- Quick menu sorted **A–Z** (categories and prompts). Empty categories are hidden from the quick menu.
- Backup schedule: **once a day while running, and on exit** — only when the library changed since the last backup. Keep the **last 30** backups; delete older ones from the backup folder (only files matching the app's own backup naming pattern).
- Startup with Windows via the per-user `HKCU\...\Run` registry key, toggle in Settings, **on by default**.
- Closing the main window **hides it to the tray**; Exit is in the tray menu.
- After a copy, a brief **"Copied: <title>"** confirmation (tray balloon/toast).
- Distribution: single self-contained `.exe` (`dotnet publish` self-contained, single-file). No installer in v1.

## 6. How it works

### 6.1 Data model

```
Category { Id (Guid), Name (string, unique, case-insensitive) }
Prompt   { Id (Guid), Title (required), Description (optional),
           CategoryId (required), Text (required),
           CreatedAt, UpdatedAt }
Library  { SchemaVersion (int), Categories[], Prompts[] }          -> library.json
Settings { Hotkey, BackupFolder, StartWithWindows, BackupsToKeep,
           LastBackupAt, LastBackedUpLibraryHash }                 -> settings.json
```

Include `SchemaVersion` so future changes (e.g. import, placeholders) can migrate the file. "Changed since last backup" = hash of `library.json` differs from `LastBackedUpLibraryHash`.

### 6.2 Quick menu

- Real Windows context menu (WPF `ContextMenu` or WinForms `ContextMenuStrip` — builder's choice), opened at the mouse cursor position.
- Structure:
  ```
  Category A        ›   Prompt 1
  Category B        ›   Prompt 2
  …
  ────────────
  Open Prompt Library…
  Exit
  ```
- Hover a category → submenu of prompt titles. Click a prompt → its exact text goes to the clipboard as Unicode text, menu closes, "Copied" confirmation shows.
- Focus must return correctly so the user can immediately press Ctrl+V in the app they were in. (Known Windows quirk: tray-opened menus need the owner window brought to foreground, or they won't close when clicking elsewhere.)
- Clipboard can be briefly locked by other apps — retry a few times before reporting failure.
- If there are no prompts, menu shows a disabled "No prompts yet" item plus "Open Prompt Library…".

### 6.3 Tray icon

- Left-click → quick menu.
- Right-click → same quick menu (same items; Settings reachable from main window).
- Double-click → open main window (builder may drop this if it clashes with left-click handling).

### 6.4 Main window (three columns)

Mockup: `.superpowers/brainstorm/*/content/main-window-layout.html`, option A (local only, git-ignored).

**Left — Categories**
- "All prompts" at top, then categories A–Z, each with a prompt count.
- "+ New category" at the bottom.
- Right-click a category → Rename / Delete.
- Deleting a category with prompts: ask which category to move them to (or cancel). Never delete prompts as a side-effect.
- Category names must be unique and non-empty.

**Middle — Prompts**
- Prompts in selected category (title bold, description underneath, "No description" in italic when empty).
- Search box at top: filters by title, description and prompt text, case-insensitive, within the current category ("All prompts" = everything).
- "+ New prompt" at the bottom — creates a prompt in the selected category (first category if "All prompts" is selected).

**Right — Editor**
- Title (required), Description, Category (dropdown of existing categories only), Prompt (large multi-line box, monospace font, accepts Tab and Enter, no auto-formatting).
- Buttons: Delete (confirm first), Copy (same as quick menu copy), Save.
- Switching prompts or closing the window with unsaved changes → "Save changes?" (Save / Don't save / Cancel).

**Menu bar / toolbar** (light): File → Back up now, Restore from backup…, Settings…, Exit.

### 6.5 Settings window

- Shortcut (key-capture field).
- Backup folder (folder picker). On first run, if `G:\My Drive` (Drive for desktop default) exists, suggest `G:\My Drive\Prompt Library Backups`; otherwise prompt the user to choose. Show a clear warning in the main window if no backup folder is set.
- Backups to keep (default 30).
- Start with Windows (checkbox, default on).

### 6.6 Backup and restore

- Backup = copy of `library.json` named `PromptLibrary-backup-YYYY-MM-DD_HHmmss.json` in the backup folder.
- Triggers: daily while running, on exit, and "Back up now". Skip if nothing changed since the last backup (except "Back up now", which always runs).
- Failure (folder missing, drive offline) → tray notification; retry next trigger. Never block the user.
- Restore: list backups in the folder (newest first, with date and prompt count), pick one, confirm. **Before restoring, back up the current library first** so a restore can be undone.

### 6.7 First run

- Create `%APPDATA%\PromptLibrary\`.
- Seed the library with the example prompts in Appendix A (useful for development and as a demo; owner can delete them).
- Register hotkey, add to startup, ask for backup folder (see 6.5).
- Show a one-time tray notification: "Prompt Library is running. Press Ctrl+Alt+P or click this icon."

## 7. Quality

- Unit tests (xUnit) for: JSON load/save round-trip (including the tricky text in Appendix A and LF→CRLF normalisation), atomic save, unreadable-file handling (never overwritten), backup naming/retention/pruning, restore (including pre-restore backup), category delete-with-move, search filtering.
- Manual check of the quick menu in: Windows Terminal, Chrome/Edge, VS Code, Claude Code, Claude desktop — copy then Ctrl+V pastes the exact text.
- Keep the code simple and conventional (MVVM-lite is fine) so the owner can maintain it.

## 8. Left to the builder

- Project structure, MVVM approach/library (e.g. CommunityToolkit.Mvvm or none), tray library (e.g. H.NotifyIcon or WinForms `NotifyIcon`).
- Visual styling details beyond the mockup (fonts, spacing, colours, light theme is fine).
- Exact toast/notification mechanism.
- Hotkey implementation (`RegisterHotKey` via P/Invoke is the usual route).
- App icon.

## 9. Additions (2026-11-04, owner requests during build)

- **Backups to OneDrive, Google Drive, Dropbox or iCloud Drive.** The app detects OneDrive (personal and work), Google Drive for desktop (any drive letter or language), Dropbox (personal and business) and iCloud Drive folders automatically and offers them as one-click choices; any other folder can still be picked. Choosing one runs the first backup immediately and confirms it. Backups go to `<cloud root>\Prompt Library Backups`. Replaces the Google-Drive-only wording in §5 and §6.5.
- **First-run onboarding wizard** (4 steps: welcome, try the shortcut, choose backups, start with Windows), skippable, reopenable from `Help → Setup guide…`. Replaces the first-run balloon and auto-opened Settings in §6.7.
- **Installer with onboarding pages** for the direct download (per-user MSI: welcome, ready, progress, finish with "Launch Prompt Library"), which hands over to the first-run wizard. The Store version installs through the Store's own UI and then shows the same wizard.
- **Microsoft Store** distribution via MSIX (see `docs/store-readiness.md`); start-with-Windows uses the package startup task when packaged.
- **UI quality bar:** every screen scores ≥ 90% on `docs/design/ui-review-rubric.md`, built to `docs/design/ui-style.md`.
- **Edge cases:** an explicit edge-case test sweep plus a manual edge checklist (`docs/edge-cases.md`).

## Appendix A — Seed prompts

Used for first-run seed data and as test fixtures. Several deliberately contain multi-line text, quotes, code fences, `{placeholders}`, emoji and an empty description.

| # | Category | Title | Description |
|---|----------|-------|-------------|
| 1 | Coding | Explain this code | Plain-English walkthrough of unfamiliar code |
| 2 | Coding | Refactor for readability | Clean up without changing behaviour |
| 3 | Coding | Fix this error | *(empty)* |
| 4 | Testing | Write unit tests | xUnit tests covering happy path + edge cases |
| 5 | Testing | Find edge cases | Brainstorm what could break |
| 6 | User Interface | Critique this screen | Usability review of a screenshot |
| 7 | User Interface | Write empty-state copy | Friendly text for "nothing here yet" screens |
| 8 | Marketing | Product announcement post | LinkedIn launch post |
| 9 | Marketing | Landing page headline ideas | 10 headline + subheadline pairs |
| 10 | Documentation | Write a README | Project README from scratch |
| 11 | Documentation | Summarise changes for a release | Turn commit list into release notes |
| 12 | Code Review | Review my pull request | Thorough PR review with priorities |

Prompt texts:

**1.**
```
Explain what the following code does, step by step, as if I'm a mid-level developer new to this codebase. Call out anything surprising, risky, or non-obvious. End with a one-sentence summary.
```

**2.**
```
Refactor the code below for readability. Rules:
- Do NOT change behaviour or public signatures
- Prefer small, well-named functions
- Remove dead code and duplicated logic
Show the refactored code, then a bullet list of what you changed and why.
```

**3.**
```
I'm getting this error:

{paste error here}

Find the most likely root cause before suggesting a fix. Ask me for the relevant file if you need it. Don't guess.
```

**4.**
```
Write xUnit tests for the class below. Cover: the happy path, null/empty inputs, boundary values, and at least one failure case. Use the Arrange / Act / Assert pattern and descriptive test names like Method_Scenario_ExpectedResult.
```

**5.**
```
List every edge case you can think of for this feature: "{feature description}". Group them by: input, timing/concurrency, permissions, and data size. Mark the 3 most likely to cause a real bug.
```

**6.**
```
Review the attached screenshot as a senior UX designer. Comment on: visual hierarchy, spacing, contrast/accessibility, and anything a first-time user would find confusing. Rank issues by impact — highest first.
```

**7.**
```
Write 3 options for empty-state text for a screen that shows "{what the list contains}". Each option: a short heading (max 6 words) + one helpful sentence + a button label. Tone: friendly, not cute. 🙂
```

**8.**
```
Write a LinkedIn post announcing "{product name}". Audience: developers and tech leads. Lead with the problem it solves, not the features. Max 150 words, no hashtag spam (2 max), end with a clear call to action.
```

**9.**
```
Give me 10 headline + subheadline pairs for a landing page for {product}. Mix styles: benefit-led, problem-led, curiosity, and "how it works". Keep headlines under 10 words.
```

**10.**
````
Write a README.md for this project. Sections: what it is (2 sentences), screenshot placeholder, install, quick start, configuration, FAQ, license. Use code blocks like:

```bash
dotnet run
```

Keep it scannable — short paragraphs, lots of headings.
````

**11.**
```
Turn the commit messages below into user-facing release notes. Group into "New", "Improved", "Fixed". Drop internal-only changes (refactors, CI tweaks). Write for end users, not developers.
```

**12.**
```
Review this diff like a strict but fair senior engineer. For each issue give: file + line, the problem, why it matters, and a suggested fix. Label each as "must fix", "should fix", or "nit". Don't comment on formatting — we have a linter for that. Check especially for: null handling, "off-by-one" errors, and anything that's 'clever' but hard to read.
```

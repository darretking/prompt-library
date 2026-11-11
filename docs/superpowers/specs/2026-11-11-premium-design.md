# Prompt Library 1.2 — "Premium" upgrade spec

Branch: `feature/premium`. Version bump to 1.2.0 at the end. Owner delegated all decisions; rulings are marked **R#**.
Design bar (unchanged): every screen follows `docs/design/ui-style.md` (updated by this spec where noted) and must score
≥ 90 % quality / ≥ 95 % simplicity on `docs/design/ui-review-rubric.md`. Plain words, sentence case, nothing shown until needed.

Environment: Windows 11, .NET 10, WPF + CommunityToolkit.Mvvm, xUnit. Build: `dotnet build PromptLibrary.sln`.
Tests: `dotnet test PromptLibrary.sln`. Shell is Git Bash. **Never run the real app against the owner's data in
`%APPDATA%\PromptLibrary`, and never touch OneDrive/Google Drive/Dropbox/iCloud folders.** Tests use temp folders only.

## 0. Data model changes (Core) — all JSON-additive, old files still load, old clients ignore new fields

- `Prompt.Tags : List<string>` (default empty). Stored lower-case, trimmed, no `#`, unique, max 10 per prompt, max 24 chars each.
- `Prompt.SendTo : string?` — optional default send target id (see §9).
- `Category.Emoji : string?` — optional single emoji shown before the name (**R1**: emoji only, no colour picker).
- `AppSettings.Usage : Dictionary<Guid, PromptUsage>` with `PromptUsage { int Count; DateTimeOffset LastUsedAt; }`.
  Usage is per user and covers team prompts too, so it lives in settings, not in the library (**R2**).
- `AppSettings.PlaceholderValues : Dictionary<string, string>` — last value typed per placeholder name (key lower-cased).
- `AppSettings.ShortcutOpens : string` = `"search"` (default) or `"menu"` (classic native menu) (**R3**).
- `AppSettings.Theme : string` = `"system"` (default) | `"light"` | `"dark"`.
- Team file: `TeamReader`/`TeamWriter` must round-trip `Tags`, `SendTo`, `Emoji`. Check the JSON options do not reject
  unknown members (old 1.1 clients reading a 1.2 team file must still work); add a test that proves it.

## 1. Search window (replaces the menu as the shortcut's default)

- **R3**: Ctrl+Alt+P and a left-click on the tray icon open the **search window**. Right-click on tray keeps a short
  native menu: `Open Prompt Library…`, `Settings…`, `Exit`. Settings gets "Shortcut opens: (•) Search window ( ) Menu".
  "Menu" restores today's exact native category menu (keep `QuickMenuBuilder`).
- Window: borderless, 640 px wide, ~420 px tall max, centred on the monitor holding the mouse, rounded corners, Mica/acrylic
  where available (falls back to `Brush.Surface`), shadow from DWM. Topmost, not in taskbar, closes on Esc and on deactivate.
- Layout: search box at top (placeholder "Search prompts"), result list below (title semibold + muted description +
  muted category/“Team” label), right-hand preview pane (monospace, read-only, first ~20 lines) when width allows.
- Empty query: **Pinned** group, then **Recent** (top 8 by last used), then everything else A–Z. Typing: ranked results (§6).
- Keys: ↑/↓ move, Enter = use prompt, Ctrl+Enter = send to default target (§9), Ctrl+O = open in main window, Esc = close.
  Footer hint row in muted 12 px: `Enter paste · Ctrl+Enter send · Ctrl+O open · Esc close`.
- "Use prompt" = if it has placeholders → fill-in form (§2); else copy + paste into previous window exactly like today
  (`AutoPaster`, honouring `PasteAfterCopy`). The previous foreground window must be captured **before** the search
  window takes focus and restored before paste.
- Record usage (§5) on every use from search window, quick menu, and main-window Copy/Send.
- Team prompts hidden by the user are excluded; team prompts included otherwise.

## 2. Placeholder fill-in

- Syntax (Core `PromptTemplate`): `{name}` → text field. `{a / b / c}` (2+ options split on ` / `) → dropdown with those
  options plus free typing. `{label, e.g. hint}` → field named `label` with hint text `e.g. hint`. `{{` and `}}` are literal
  braces and never placeholders. Same name used twice = one field, filled everywhere. Names are matched case-insensitively.
  Text with no placeholders → no form. Unclosed `{` → literal.
- `PromptTemplate.Parse(text) -> IReadOnlyList<TemplateField>` and `PromptTemplate.Fill(text, IDictionary<string,string>)`.
  Empty value leaves the original `{…}` in place (so the user sees what was skipped).
- Form appears **inside the search window** (replaces the list), one row per field, labels above fields, first field
  focused, prefilled with remembered value from `PlaceholderValues` (for dropdown fields, the remembered option).
  Live preview of the filled prompt below. Enter on last field / `Paste` button = fill + copy + paste. Esc = back to list.
  Main window Copy on a template prompt opens the same form as a small dialog (same view, reused).

## 3. Editor highlighting + preview

- **R4**: Replace the prompt `TextBox` with AvalonEdit (`AvalonEdit` NuGet, MIT) wrapped in a small bindable control so
  `PromptEditorViewModel.Text` still binds two-way. Keep: Cascadia Mono 13/20, exact text round-trip (tabs, CRLF handling
  via existing `EditorText`/`LineEndings`), undo, the 4 px Brand left edge, char/line count, all existing keyboard shortcuts
  and dirty-tracking. Word wrap on. No line numbers.
- Highlighting: placeholders as soft Brand chips (BrandSoft background, Brand text), Markdown `#` headings semibold,
  ``` fenced code ``` muted background, `**bold**` bold. Colours come from theme tokens (works in dark mode).
- "Preview" toggle (segmented `Edit | Preview` above the slip, right of the label) shows the prompt with placeholders
  rendered as chips and with remembered values substituted, read-only. Default is Edit.

## 4. Theme

- **R5**: Light + dark palettes, following Windows (`AppsUseLightTheme` registry, live via `SystemEvents.UserPreferenceChanged`)
  unless Settings overrides (`Theme`). All brushes become `DynamicResource`. Dark tokens:
  Window `#1C1F26`, Rail `#161920`, Surface `#242833`, Ink `#E8EAF0`, Muted `#A0A8B8`, Line `#353B48`, Brand `#8B85F0`
  (text/edges), BrandFill `#4338CA` (Copy button fill, white text), BrandSoft `#27264A`, Danger `#F97066`,
  Warn `#3A2F12`/text `#F5D48A`. All text ≥ 4.5:1 — verify with a small contrast test in App.Tests.
- Windows 11: Mica backdrop (`DWMWA_SYSTEMBACKDROP_TYPE`), dark title bar (`DWMWA_USE_IMMERSIVE_DARK_MODE`), rounded corners
  (`DWMWA_WINDOW_CORNER_PREFERENCE`). Windows 10: silently skip. One helper applied to every window.
- ui-style.md: update the token table, drop "quick menu stays native" (it's now optional), allow 150 ms fades for toasts.

## 5. Usage ranking

- Core `UsageTracker`: `Record(settings, promptId, now)`, `Frecency(usage, now)` = count weighted by recency
  (half-life 14 days). Prune entries for prompts that no longer exist on load.
- Main window rail: new `Recent` item under `All prompts` (clock glyph), lists up to 10 last-used prompts, most recent first.
  Hidden when nothing used yet.

## 6. Better search (Core `FuzzySearch`)

- Query syntax: `cat:<name>` (prefix match on category name), `#tag`, free words. Filters AND together.
- Free words: each word must match title, description, text or tags, by (in order of score) exact word, prefix, substring,
  subsequence/fuzzy with ≤ 1 typo for words ≥ 4 chars. Word order free. Title hits weigh most, then tags, description, text.
- Final rank = match score + small frecency bonus; ties A–Z.
- Return `SearchResult(Prompt, IsTeam, IsHidden, double Score, IReadOnlyList<(int Start,int Length)> TitleMatches,
  DescriptionMatches)` so views can bold matched characters.
- Main window search uses it (keeps existing both-libraries behaviour and hidden team prompts greyed). Search window uses it.

## 7. Tags, emoji

- Editor: `Tags` row under Description — chip input: type, Enter/comma adds chip, Backspace on empty removes last, × on chip.
- List rows show up to 3 tags as small muted chips after the description.
- Category emoji: rename/new-category inline editor gets a small emoji button (opens a 6×6 grid of ~36 curated emoji +
  "None"). Rail, category combobox, quick menu, search window show `emoji name`. Seed categories get default emoji.
- Team prompts: tags/emoji visible; editable only in team edit mode (existing rules).

## 8. Version history (personal prompts only — team already has history) — **R6**

- Core `PromptHistoryStore` in `%APPDATA%\PromptLibrary\History\<promptId>.json` (path injected; tests use temp dir).
  On each successful save that changed title/description/text/tags, append the **previous** snapshot
  `{ SavedAt, Title, Description, Text, Tags }`. Keep last 50. Delete the file when the prompt is deleted.
  Writes use the existing `AtomicFile` helper. History failures never block a save (log to crash log).
- Editor overflow menu → `Version history…` opens a window: list of versions (relative date + first line) on the left,
  a line diff (added green / removed red, theme-aware) against the current text on the right, `Restore this version`
  (loads it into the editor as unsaved changes; user still presses Save).

## 9. Send to an AI app — **R7**

- Core `SendTargets`: ids `chatgpt`, `claude`, `copilot`, `perplexity` with names and URL builders:
  `https://chatgpt.com/?q=`, `https://claude.ai/new?q=`, `https://copilot.microsoft.com/?q=`,
  `https://www.perplexity.ai/search?q=` + `Uri.EscapeDataString(text)`. If the encoded URL > 6000 chars, open the base
  site instead. **Always** also copy the text to the clipboard, and show status
  `Opened ChatGPT. The prompt is also on your clipboard.`
- Main window: `Send to ▾` split button next to Copy (Copy stays the only filled button). The menu lists targets, a check
  marks the prompt's default, and `Set as default for this prompt`. Search window: Ctrl+Enter sends to the prompt's
  default, or ChatGPT if none (**R8**). Placeholders are filled first.
- README privacy section: add one sentence: sending opens that site in your browser with the prompt text; nothing is sent
  unless you choose Send.

## 10. Main window polish

- Title becomes an in-place 18 px semibold `TextBox` styled flat (no border until hover/focus); Description the same at
  13 px muted, placeholder "Add a description". Category becomes a compact chip-styled ComboBox on the same row as Tags.
  Field labels removed for these three; AutomationProperties.Name kept.
- Bottom bar: left = `…` overflow button (menu: `Pin`/`Unpin`, `Version history…`, `Move to category…`,
  separator, `Delete prompt` in Danger). Right = `Save` (outline), `Send to ▾` (outline), `Copy prompt` (filled).
  Team-specific buttons (Hide/Copy to yours etc.) move into the same overflow menu.
- List rows: pin glyph (Segoe Fluent Icons ``) at right for pinned; existing team "changed" marker stays.
  Hover background `BrandSoft` at 50 % (**R9**: hover allowed now).
- Toast: `Copied` / `Sent` feedback as a small pill bottom-centre of the window, 150 ms fade in/out, 1.5 s. Copy button
  still flips to `Copied`.
- Empty states get a simple 48 px Segoe Fluent glyph above the existing text.
- Every button/icon has a tooltip with its shortcut where one exists (Ctrl+Shift+Enter = Send to default).

## 11. Delete category: option to delete its prompts too — **R10**

- Delete-category confirm dialog for a non-empty category shows today's move-to choice **plus** a checkbox
  `Also delete the N prompts in this category`. When ticked, the move-to picker disables and the primary button becomes
  `Delete category and N prompts` in Danger style. Empty category: no checkbox.
- Core `LibraryEditor.DeleteCategory(Guid id, Guid? movePromptsTo, bool deletePrompts = false)`. Deleting prompts also
  unpins them, clears their usage entries and history files. Team side follows the same rules in team edit mode.
- A safety copy is not needed (daily backups + existing safety-copy rules), but the status bar says
  `Deleted "<name>" and N prompts.`

## 12. Added after web research (2026-11-11)

Product research (speed, reach, reliability and trust beat visual polish) and Windows 11 design research (Fluent
guidance, PowerToys Command Palette). Sources are listed in `docs/design/premium-review-results.md`.

- **Dynamic tokens** (Core `PromptTemplate`): `{clipboard}` = current clipboard text, `{date}` = today in the user's
  short date format, `{time}` = short time, `{cursor}` = removed on fill. When pasting into another app, the caret ends
  at that spot (send ← key presses for the remaining character count after paste; skip if the count is > 2000).
  These names are reserved: they never appear as form fields. Token values are injected through a provider interface so
  tests can control them.
- **Import / export** (File menu): `Export prompts…` writes one `.json` file (same schema as library.json plus a
  `"format": "prompt-library-export/1"` marker) or a folder of `.md` files, one per prompt, with YAML front matter
  (title, description, category, tags) and grouped in category subfolders. `Import prompts…` reads that JSON or a folder
  of .md/.txt files (a file name becomes the title; the parent folder becomes the category). Duplicate titles in the same
  category are skipped and counted. Summary dialog: `Imported 12 prompts. Skipped 3 duplicates.` Personal library only.
  (**R11**)
- **Paste reliability**: Settings option `Put back what was on the clipboard after pasting` (on by default). After an
  auto-paste, restore the previous clipboard text after 500 ms. Only plain text is restored; when other formats were
  present, restore nothing. Never do this when the user only chose Copy (no paste).
- **Footprint**: create the search window once at startup, hidden, and reuse it. It must show within 150 ms of the
  shortcut. Don't reload the library per open.
- **Skipped** (**R12**): a keyword or hotkey per prompt (keyboard-hook reliability risk), cloud sync, and in-app AI runs.

### Windows 11 visual rules (adds to §4/§10 and ui-style.md)

- Corner radius: 8 px for windows, dialogs, flyouts, menus and the search window. 4 px for buttons, text boxes, list
  rows, chips and toasts. An inner radius is never equal to or bigger than its container's.
- Spacing: 4 px grid (4/8/12/16/24). Controls are 32 px tall; list rows are ≥ 40 px.
- Type: keep 13 px body (**R13**: an existing, tested layout). Use semibold, never bold, and no other font sizes.
- Icons: Segoe Fluent Icons only (fallback to Segoe MDL2 Assets on Win10), 16 px, one icon set. Emoji appear only as
  user content (category emoji).
- Materials: Mica only on the main and settings windows. The search window uses Acrylic
  (`DWMSBT_TRANSIENTWINDOW`). Content layers stay solid. Use a solid fallback when transparency is off, the window is
  inactive, or the build is < 22621.
- Depth: 1 px `Brush.Line` strokes instead of shadows. DWM draws the window shadow.
- Motion: 83 / 167 / 250 ms. Enter easing cubic-bezier(0,0,0,1), exit (1,0,1,1). Toasts fade 167 ms. Search window
  appears with a 167 ms fade and an 8 px slide-up. Respect `SystemParameters.ClientAreaAnimation` (off = no animation).
- States: every custom control has rest, hover, pressed, disabled and focus visuals. Focus is a 2 px Brand ring.
- Search window extras (from Command Palette): Alt+1…9 runs the Nth visible result. Esc clears the query first, then
  closes. Backspace on an empty form goes back to the list. Ctrl+K opens the result's actions menu (Copy, Paste, Send to…,
  Open in main window, Pin).
- Main window: F6 moves focus between the rail, list and editor panes.
- **Start with Windows** (owner request): setup's last step asks it as a clear question ("Start Prompt Library when
  Windows starts?" with a "Recommended" explanation, ticked by default). Settings keeps the `Start Prompt Library when
  Windows starts` checkbox. Any Settings reorganisation must keep it near the top, under a "General" group.

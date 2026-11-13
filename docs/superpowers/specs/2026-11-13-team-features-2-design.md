# Team prompts 2 — design

Date: 2026-11-13 · Status: **approved** (owner delegated all decisions, 2026-11-13) · Target release: **v1.3.0** · Builds on 1.2.4
Branch: `feature/team-2`. Rulings are marked **T#**. Design bar unchanged: `docs/design/ui-style.md`, and ≥ 90 % quality /
≥ 95 % simplicity on `docs/design/ui-review-rubric.md`. Plain words, sentence case, nothing shown until needed. Solo users
(no team joined) see nothing new.

Environment: Windows 11, .NET 10, WPF + CommunityToolkit.Mvvm, xUnit. `dotnet build PromptLibrary.sln`,
`dotnet test PromptLibrary.sln`. Installer: `installer/` (WiX 5, outside the sln, `build/build-installer.ps1`).
**Never run the real app against `%APPDATA%\PromptLibrary`, never touch real OneDrive/Google Drive/Dropbox/iCloud
folders, never write the real HKCU registry in tests** (inject a registry/probe abstraction). Tests use temp folders.

Features in scope: 1 add to team library, 2 easy joining, 3 suggestions, 4 what changed + team note, 5 team fill-in
values, 6 featured prompts, 7 installer preset, 8 team library check, 11 out-of-date copies.

## 0. Data model (all JSON-additive; old files load; old clients ignore new fields)

- `Prompt.Featured : bool` (default false; only meaningful in a team file).
- `Prompt.SourceTeamPromptId : Guid?` and `Prompt.SourceFingerprint : string?` (personal copies only, §11).
- Team file top level (kept on `TeamLibrary`, parsed/serialized by `TeamFile`, preserved by `Merge`):
  - `note : { text: string, at: DateTimeOffset }?` (§4) — max 280 chars.
  - `fillInValues : { name: value }` (§5) — keys lower-cased, max 50 entries, value ≤ 200 chars.
- `AppSettings.PresetTeamSourceApplied : string?` (§7), `AppSettings.SeenTeamNote : string?` (hash of the note last
  dismissed, §4).
- New sidecar `%APPDATA%\PromptLibrary\team-seen.json` (path injected): per team prompt id the snapshot
  `{ title, description, text, categoryName }` of the version this member last saw (§4). Written with `AtomicFile`.
  Cleared with the other team state on Leave / join of a different source.
- **T1**: schemaVersion stays 1. New fields are optional and unknown-tolerant, so 1.2 members read a 1.3 file fine,
  and `Merge` keeps fields a 1.2 editor doesn't know.

## 1. Add to team library (editors)

- **T2**: personal prompt ⋯ menu gets **Copy to team library** when a team is joined and this PC can edit it
  (`CanEdit`). One click, no dialog, like Make my copy: goes into the team category of the same name (created if
  missing), new id, through the normal guarded team save (conflict check, history copy). Does **not** need edit mode.
  Status: `Copied to team prompts · Sales`. On `ChangedByOther`: reload and retry once automatically (it's an add,
  so it can't clobber), then report.
- **T3**: **Import prompts…** works on the Team side while editing: same file picker and `PromptImport` pipeline,
  targeting the team `Library`, one save for the whole import, same results dialog. Title says
  `Import into team prompts`. Export works on the Team side for anyone (read-only is fine).

## 2. Easy joining

- **Copy invite** (Team header ⋯, anyone joined): puts a short message on the clipboard:
  ```
  Join our team prompts in Prompt Library.
  1. Get the app: https://github.com/darretking/prompt-library/releases/latest
  2. In Prompt Library open Settings → Team prompts → Join, and paste:
  <source>
  ```
  `<source>` is the https link for URL sources. For file sources it is the path **relative to the cloud folder**
  when the file is inside a detected cloud root, written as `OneDrive - Acme\Shared\Prompts` (**T4**: never the
  inviter's own `C:\Users\name\…` path, which is wrong on every other PC); otherwise the full path (network share).
  Status: `Invite copied. Paste it into an email or chat.`
- **Join accepts cloud-relative paths**: `TeamSource.FromInput` gets an optional resolver: input that starts with a
  detected cloud folder's display/folder name (e.g. `OneDrive - Acme\Shared\Prompts`) is resolved against that root.
- **Found on this PC**: the Join dialog lists `team-prompts.json` files found under detected cloud roots (Core
  `TeamFileFinder`: depth ≤ 5, skips hidden/system folders and `node_modules`, `.git`, stops at 50 hits or 3 s,
  runs off the UI thread, cancellable). Each row: library name (read from the file) + short location; click fills the
  box. Hidden when nothing is found. Never shown in tests against real folders (inject `ICloudProbe` + file system).
- **Open a team file** (**T5**, replaces "double-click"): the app never takes over `.json`. The installer registers
  Prompt Library under *Open with* for `.json` (`HKCU\Software\Classes\Applications\PromptLibrary.exe\SupportedTypes`).
  `PromptLibrary.exe <path>` / `--join <path-or-url>`: a running instance receives the argument over a named pipe
  (`PromptLibrary.Join` + `Paths.InstanceSuffix`, current-user ACL, one message ≤ 4 KB, UTF-8, then closed) and opens
  the Join dialog **prefilled** — the user still presses Join (**T6**: nothing joins from an argument without a click).
  A first launch with the argument does the same after start-up. A path that isn't a team file shows the normal
  Join error.

## 3. Suggestions (members → editors)

- Personal prompt ⋯ menu: **Suggest for the team…** (team joined, any member). Small dialog: the prompt title
  (read-only), team category (combo of team categories + the prompt's own category name), optional note (≤ 280),
  `Your name` (prefilled with the Windows display name, editable, may be blank). Button `Send suggestion`.
- File sources: writes `Prompt Library suggestions\<guid>.json` next to the team file:
  `{ kind: "prompt-library-suggestion", id, title, description, text, tags, categoryName, note, from, createdAt }`.
  Status `Suggestion sent to the team.`
- **T7**: URL source or folder not writable → the suggestion is put on the clipboard as plain text (title, category,
  text, note) and the status says `Couldn't reach the team folder. The suggestion is on your clipboard to send to the
  team owner.` No email integration.
- Editors (CanEdit): Team header shows **Suggestions (n)** when n > 0 (read on each refresh; file sources only; at
  most 200 files, ≤ 64 KB each, bad files ignored). Opens a Suggestions window: list (title, from, age) on the left;
  right: text preview, note, category combo (defaults to the suggested name), **Add to team prompts** (filled) and
  **Dismiss**. Add = guarded team save, then the file moves to `Prompt Library suggestions\done\` (keep newest 50).
  Dismiss = same move. **T8**: suggestion files are untrusted input: size caps, JSON only, text never executed,
  title/category trimmed to the normal limits.

## 4. What changed + team note

- On join (and baseline), and whenever a prompt's marker is cleared (opened / Mark all as seen), store its snapshot
  in `team-seen.json`. A team prompt marked **Updated** shows a **What changed** link next to the marker in the
  editor. It opens a diff window built from the existing history diff styles (`LineDiff`, `Brush.DiffAdded/Removed`):
  title/description/category changes as a short list on top, the text as a line diff below. Opening it also clears
  the marker. **T9**: no snapshot (e.g. joined before 1.3) → the link is hidden, nothing else changes.
- **Team note** (**T10**: one library-level note instead of a note per save — no extra step on every save): editors
  get Team ⋯ → **Note for everyone…** (multi-line box, 280 chars, `Save note` / `Remove note`). Members see it as a
  muted line under the Team header, `Note: <text> · <age>` with a `×` that dismisses it until the note changes.

## 5. Team fill-in values

- Editors: Team ⋯ → **Fill-in values for everyone…**: a two-column list (`Field`, `Value`), prefilled with every
  field name used in team prompts (dynamic tokens excluded), plus `Add a field`. Saved with the guarded team save.
- **T11**: lookup order when filling a field: the member's own remembered value → the team value → empty. Applies to
  the fill-in form, the search-window form, the menu-mode dialog and Preview. A field prefilled from the team shows a
  muted `Team value` hint under it.

## 6. Featured prompts

- Editors (in edit mode): prompt ⋯ menu **Feature for everyone** / **Stop featuring** (max 10; the 11th says
  `Up to 10 prompts can be featured.`).
- Members see them: shortcut menu block **Featured** after Pinned (team icon, hidden prompts left out); search window
  empty-query group **Featured** after Pinned; Team side rail item **Featured** (star glyph) under All prompts, shown
  only when any exist. List rows show a small star. **T12**: members can hide a featured prompt (it then leaves their
  Featured block); they can't unfeature it.

## 7. Installer preset

- MSI property `TEAMSOURCE` (public, `Secure="yes"`): when set, writes `HKCU\Software\Prompt Library\TeamSource`.
  Example: `msiexec /i PromptLibrary-Setup.msi TEAMSOURCE="\\server\share\Prompts"`.
- App start: no team joined, the value exists and differs from `PresetTeamSourceApplied` → join it in the background
  through the normal `JoinAsync` (validation unchanged). Success → `PresetTeamSourceApplied = value`. Failure → retry
  at next start, quietly. **T13**: once applied, leaving the team is respected; only a *different* preset value joins
  again. The registry read goes through an injected interface.
- docs/DEVELOPMENT.md and README "For IT" paragraph document it.

## 8. Team library check (editors)

- Core `TeamHealthCheck.Run(Library) -> IReadOnlyList<HealthIssue(Kind, PromptId?, CategoryId?, Message)>`:
  duplicate titles in a category (case-insensitive), identical text (whitespace-normalised) across prompts, empty
  categories, prompts with no description, the same field name used with different choice lists, likely broken fields
  (`{name` not closed on the same line, `{}`), titles over 80 chars. Ordered by severity: duplicates, broken fields,
  empty categories, the rest.
- Team ⋯ → **Check the team library** (CanEdit): dialog with the list (plain sentence per issue). Clicking an issue
  enters edit mode if needed and opens that prompt / category. Empty result: `Everything looks good.`

## 11. Out-of-date copies

- Make my copy stores `SourceTeamPromptId` and `SourceFingerprint` (= `TeamUserState.Fingerprint` of the team prompt at
  copy time). Copy to team library (§1) does not set them.
- When a personal prompt's source team prompt still exists and its fingerprint differs, the personal editor shows a
  quiet banner: `The team updated this prompt.` with **See changes** (diff: your copy → team version) and
  **Update my copy**. Update replaces title, description, text and tags (history keeps the old version, as for any
  save) and stores the new fingerprint. **T14**: if the personal copy was edited since copying, Update asks
  `Replace your changes with the team's version?` (history still has them). A `×` on the banner = keep mine: stores the
  new fingerprint so it stops asking until the team changes it again. Source deleted → banner never shows.

## Out of scope

Several team libraries, usage counts for owners, sign-in links, edit locks, per-category roles, settings locks,
protocol links (`promptlibrary://`), email sending.

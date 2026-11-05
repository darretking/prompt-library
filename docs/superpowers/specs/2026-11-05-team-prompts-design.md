# Team prompts — design

Date: 2026-11-05 · Status: **approved**. The owner confirmed the brainstorm meets their 95 % bar and delegated all further decisions to Claude (2026-11-05). · Target release: **v1.1.0** · Builds on v1.0.2

## Why

Prompt Library today is one person's library. Small teams want everyone to use the same proven prompts
("best practice") while still keeping their own. The team owner curates a shared set. Members get it
automatically, can't change it, and can always tell it apart from their own prompts.

## Who it's for (owner decisions)

- **First audience:** a small team (about 5–50 people) with **one owner** who curates the shared prompts and keeps
  them in a shared folder (network drive, or a OneDrive / Google Drive / Dropbox synced folder) or publishes
  them at a public https link.
- **Name in the app:** **"Team prompts"** next to **"Your prompts"**.
- **Size:** up to about **200 team prompts in about 20 categories**. Design and test for that.
- **One team library per person.**

## Goals

1. A member connects once and from then on has the team's prompts in the main window, in search and in the
   shortcut menu. Those prompts update automatically and keep working offline.
2. Team prompts can't be edited or deleted by members, and it's always obvious which prompts are the team's.
3. The owner (anyone with write access to the shared file) creates and maintains the team library inside the
   app. No hand-editing of files.
4. Nobody loses work. Two editors can't overwrite each other, and every team save can be rolled back.

## Non-goals (v1.1)

- Several team libraries at once.
- Links that need a sign-in (private SharePoint / OneDrive / Google Drive URLs). Those teams use the synced-folder route.
- Owner extras: "featured" prompts pinned for everyone, a list of who's connected, or locking members' settings.
- Push notifications or pop-ups when the team library changes.
- An installer property or IT policy that pre-configures the team library.
- Merging same-named categories from both libraries.

## Anti-goals (a failure even if it technically works)

- **Confusing which is which.** A member can't instantly tell team from personal, or thinks they edited a team prompt.
- **Owner finds it fiddly.** Setting up or updating the team library needs JSON, paths typed by hand, or other
  technical knowledge.
- **Slower everyday use.** The shortcut menu or app gets slower or more cluttered, especially for people who
  never join a team.
- **Lost or clobbered work.** One editor's save wipes another's, or a member loses personal prompts.

## Owner decisions (2026-11-05)

| Topic | Decision |
|---|---|
| Who curates | One owner per team, and anyone the folder's sharing lets write to the file. |
| How it's authored | In the app. On the Team side, **Edit team library** unlocks editing and each save writes the shared file. |
| Owner's normal view | Locked like everyone else's, until they press Edit team library. |
| Who may edit | **Folder permissions decide.** If this PC can write to the shared file, the edit button shows. URL sources are always read-only. |
| Connecting | **Settings → Team library** and an optional, skippable **setup-guide step**. |
| Sources | A shared file in any folder (network, synced cloud folder), or a **public https URL** (no sign-in). |
| Refresh | **Live with an offline copy.** Re-read at start, every hour, and right away when a local/synced file changes. The last good copy is kept for offline use. |
| Main window | **Switch at the top:** "Your prompts" / "Team prompts". Only one library shows at a time (mockup C). |
| Shortcut menu | **Team block on top** (Menu 1): team categories first, each with a team icon, then a separator, then yours. |
| Member actions on team prompts | Copy and paste as usual, **Copy to your prompts** (editable copy), **Hide**, **Pin**. |
| Search | Searches **both** libraries. Team results carry the team icon. Clearing search goes back to the side you were on. |
| Pins | **Anyone can pin any prompt** (theirs or the team's). Pinned prompts form a "Pinned" block at the very top of the shortcut menu, shown only once something is pinned. |
| Hidden | **"Show hidden (n)"** toggle on the Team side lists hidden items greyed out, each with **Unhide**. |
| Change notices | **Quiet markers.** New or changed team prompts show "New" / "Updated" until opened once, and the Team switch shows a dot. No pop-ups. |
| Unreachable source | **Last copy + quiet note** at the top of the Team side: "Last updated 2 days ago · can't reach the team library · Retry". |
| Library name | The switch always reads "Team prompts". The library's own name (e.g. "Acme best practice") and its last-updated time show as a muted line at the top of the Team side and in Settings. |
| Entering edit mode | **Unlock on the Team side**, with a banner "Editing team library: changes go to everyone" and a **Done** button. |
| Publishing | **Each save goes live.** |
| Two editors | **Detect and refuse to overwrite.** If the file changed since it was loaded, the save is refused, the library reloads, and the editor reapplies their change. |
| Strength of that promise | **Best effort with a safety net.** Catch what this PC can see. Every save keeps a history copy, so a clobbered change can be restored. If a sync tool leaves a "conflict copy" of the team file, editors see "Two versions found · review" and can pick which to keep. No edit lock. |
| Hidden prompts in search | **Still found, greyed, with Unhide.** Hidden only removes them from the shortcut menu and the normal Team list. |
| Team-library safety | **Copies before each save** (keep about 30), **Restore older version** for editors, and **confirm deletions** ("This removes it for everyone"). |
| Creating | **Settings → Team library → Start a team library:** pick the shared folder, name it, optionally copy in some of your own categories. |
| Leaving | **Team prompts disappear.** Copies made into your prompts stay. Pins and hides on team prompts are forgotten. |
| Solo users | **Nothing visible** until they join or start a team library, apart from the Settings section, the setup-guide step and pinning (which shows nothing until used). |

## How it works

### Team library file

- **One file**, `team-prompts.json`, in the shared folder. A connection points at the file. Choosing a folder
  that contains it also works.
- **Format:** the same as `library.json` (`schemaVersion`, `categories`, `prompts`; prompt and category IDs are
  GUIDs, and prompts carry `createdAt` / `updatedAt`), plus a `kind: "prompt-library-team"` marker, a `name`
  (e.g. "Acme best practice") and an `updatedAt`. Members ignore unknown fields so later versions can add some.
- **Stable IDs** are what pins, hides and the "New" / "Updated" markers attach to. Editing keeps IDs, and
  "Copy to your prompts" creates new ones.
- **History:** before each save, the editor's app copies the current file into a `Prompt Library history`
  folder next to it, keeping the newest 30. Restore offers these.
- A URL source serves the same JSON.

### On each PC

- **Settings** (`settings.json`, per PC): the team source (path or URL) and the pinned prompt IDs (both
  libraries). The hidden IDs and the "already seen" `updatedAt` per team prompt (for the markers) are stored
  in `settings.json` or a small sidecar file; the builder decides which.
- **Offline copy:** the last good team file is kept at `%APPDATA%\PromptLibrary\team-cache.json`, plus when it
  was fetched and from where.
- **Personal backups** stay exactly as they are and never include team prompts (the shared file is the master
  copy; its history is the team's backup).

### Refresh and reading

- **When:** at start, every hour, and on a file-change notice for a local or synced path (debounced).
  **Retry** reads at once.
- **URL fetch:** https only, no sign-in, a short time limit, the app's normal `PromptLibrary/<version>`
  User-Agent, and a sensible size cap. Use the same caution as the update checker: never follow non-https
  redirects, and never pass anything from the file to the shell.
- **Bad or unreadable data** (bad JSON, wrong `kind`, a file mid-sync) **never replaces the last good copy**.
  It shows as "can't reach / can't read the team library" in the quiet note.
- **Speed:** reading must never block the UI or the shortcut menu. The menu uses whatever copy is loaded.

### Main window

- **Switch** at the top (segmented): "Your prompts" | "Team prompts" (with a team icon and a change dot). It only
  appears once a team library is connected.
- **Team side:** the same rail / list / editor layout. The editor is read-only with a lock note such as
  "Team prompt · Acme best practice · read only". Its buttons are **Copy prompt** (primary) and
  **Copy to your prompts**.
  - It works in **one click**, with no dialog. It copies the prompt into your category of the same name, creating
    that category if needed, and switches to Your prompts with the copy open.
  - The status line says "Copied to your prompts · Sales". The copy can be moved like any prompt (Category box).
  - **Pin** and **Hide** are buttons in the editor's button row, with the same actions on the row's right-click
    menu.
- The **header line** on the Team side shows the library name and "updated …", or the offline note with Retry.
- **Show hidden (n)** sits at the foot of the team list.
- **Search** covers both libraries. Results from the other side show its icon, and opening one switches sides.
- **Edit team library** shows on the Team side only when this PC can write to the file. Editing then works like
  personal editing (new / rename / delete category, new / edit / delete prompt). Deletions confirm "This removes
  it for everyone". **Done** locks it again.

### Saving the team library (editors)

1. Before writing, re-read the shared file. If it changed since this editor loaded it (compare its content hash
   or `updatedAt`), refuse: say "Someone else changed the team library. It's been reloaded. Please make your
   change again." and reload.
2. Copy the current file into history (keep 30).
3. Write atomically (temp file, then replace, as `AtomicFile` does for personal saves) with a new `updatedAt`.
4. Refresh this PC's own offline copy.

### Shortcut menu

From top to bottom:

1. "Update to X…" (existing, when an update is available)
2. **Pinned** prompts, flat, when any are pinned
3. **Team categories**, each with a team icon (hidden prompts left out)
4. Separator
5. Your categories (as today)
6. Separator
7. Settings…, Open…, Exit

### Settings → Team library

| State | What it shows |
|---|---|
| Not connected | **Join a team library** (one box: shared folder, file or link, plus Browse…) and **Start a team library** (folder and name, plus an optional, collapsed "copy some of your categories in"). |
| Connected | Library name, source, last updated, status, **Retry**, and **Leave team library** (confirm). |

The **setup-guide step** is optional and skippable: "Does your team share prompts?" offers Join / Start /
Skip. It is placed after the shortcut step.

### Leaving

Removes the source, the offline copy, team pins, hides and markers. Your prompts, including any "copies to
your prompts", are untouched.

## Calls made after the builder check (Claude's calls, not owner decisions)

1. **Newer team files.** The team file carries `schemaVersion`.
   - A file with a **higher** version than this app knows is still read for viewing when its categories and prompts
     parse, with unknown fields ignored.
   - **Editing is turned off** for that file: "This team library was made with a newer Prompt Library. Update to edit
     it." An older build can then never strip newer fields from the shared file.
   - A file that can't be read at all keeps the last good copy, and the quiet note says "update Prompt Library to read
     it".
2. **Editing model.** Team editing works exactly like personal editing: an explicit **Save** per prompt, plus the
   same "Save changes?" questions.
   - While edit mode is on, **automatic refreshes are held** and apply on **Done**.
   - If a save is refused because the file changed, the editor's unsaved text is **kept in the editor**. The library
     reloads under it, and the editor can save again onto the fresh copy. Nothing typed is thrown away.
3. **New / Updated markers.**
   - On **first join**, nothing is marked: everything counts as already seen.
   - A team prompt is **New** when its ID wasn't in the last copy this member saw. It is **Updated** when its title,
     description, text or category changed.
   - A category rename marks nothing.
   - An editor's own saves never mark their own copy.
   - A marker clears when the prompt is opened, or with **Mark all as seen** on the Team side.
   - The switch dot shows while any marker is unseen.
4. **Pins and hides over time.**
   - IDs that no longer exist (the owner deleted the prompt) are **dropped silently**. Moving a prompt to another
     category keeps its pin and hide.
   - **Hiding unpins.** A hidden prompt can't be pinned.
   - The Pinned block is in **pin order**, newest last.
   - Team prompts in it carry the **team icon**, just as they do in search results and the team block.
   - In the main window, Pin and Hide are buttons in the editor and on the row's right-click menu (see Simplicity
     rule 2). Pinned rows show a small pin.
5. **Start a team library.**
   - If the folder already contains `team-prompts.json`, the app **never overwrites it**. It says "This folder already
     has a team library (Name)" and offers **Join it** instead.
   - Copied-in categories and prompts get **new IDs**, so the person's own prompts stay separate.
   - The creator is **connected automatically** and, having write access, can edit.
6. **Joining.**
   - **Join** saves the source only after a **successful first read of a valid team file**. Otherwise it says why
     ("Can't reach that folder", "That isn't a Prompt Library team file") and nothing changes.
   - Joining a **different** source replaces the old one: the offline copy, team pins, hides and markers of the old
     library are cleared, as with Leave.

## Design quality bar (owner: "simplicity, intuitiveness and design standards")

### Standards it must meet

- `docs/design/ui-style.md` covers the tokens, type, three-column layout and states. Particular rules apply here:
  - **Copy prompt stays the only filled button** in the main window, on the Team side too. Copy to your prompts,
    Pin and Hide are outline or link buttons.
  - Keyboard: a visible focus ring, the tab order, and `AutomationProperties.Name` on every new control.
  - Text contrast is at least 4.5:1.
  - Scaling at 100 %, 150 % and 200 % shows no clipped text.
- `docs/design/ui-review-rubric.md` sets two bars:
  - The **quality score** must be **≥ 90 %** on every new or changed screen: the main window's Team side, its edit
    mode, the Settings Team library section, the Join and Start dialogs, the setup-guide step and the shortcut menu.
  - The **simplicity & intuitiveness score** must be **≥ 95 %** on the main window (both sides) and the setup guide,
    and ≥ 90 % on the new dialogs.
  - Results are recorded in `docs/design/ui-review-results.md`.
- The rubric's core tasks gain these team tasks. The S2 limit is **≤ 3 actions** once the window is open.
  - Copy the team prompt "Follow-up email".
  - Find a team prompt by searching.
  - Make your own editable copy of a team prompt.
  - Pin a prompt.
  - Hide a team prompt, and get it back.
  - Tell, in 5 seconds, whether the open prompt is yours or the team's.
  - Editor only: fix a typo in a team prompt and save. This takes ≤ 3 actions after **Edit team library**,
    exactly as for a personal prompt, plus one action to unlock. The extra action is deliberate friction, since a
    change goes to everyone.
  - Joining a team library takes ≤ 3 actions from Settings → Team library: Join…, paste or Browse, Join.

### Simplicity rules (Claude's calls, applied to the design above)

1. **One way to tell team from yours, used everywhere.** One team icon (people) and the word "Team" mark team
   items in search results, the Pinned block, the shortcut menu and the editor's lock note. No second colour
   scheme and no extra badges.
2. **Quiet rows.**
   - Prompt rows stay as they are today (title + description).
   - The only extras are a small pin glyph on pinned prompts, a "New" / "Updated" tag where it applies, and the team
     icon in search results.
   - **Pin** and **Hide** live as plain buttons in the editor's button row (and on the row's right-click menu as a
     shortcut). They are not icons on every row.
3. **Read-only looks read-only, not broken.** The Team-side editor shows title, description and prompt as normal
   readable text on the slip (no greyed or disabled inputs). There's one lock line above it: "Team prompt ·
   Acme best practice · read only".
4. **One field to join.** The Join dialog has a single box, **"Shared folder, file or link"**, plus **Browse…**. The
   app works out whether it's a folder, a file or an https link. There's no source-type choice.
5. **Two fields to start.** The Start dialog asks for the folder and a name. "Copy some of your categories in" is
   an optional checkbox list, collapsed by default.
6. **Familiar patterns.**
   - The switch is a standard two-part segmented control. **Ctrl+1** shows Your prompts and **Ctrl+2** Team prompts.
   - The edit-mode banner reuses the existing quiet warning-bar style ("Editing team library: changes go to
     everyone" + **Done**).
   - The offline note reuses the muted header line. A "Two versions found" notice for editors reuses the same bar
     with **Review**.
7. **Nothing until needed.** Solo users see no switch, no team block and no Pinned block until they join, start or
   pin. "Mark all as seen" and "Show hidden (n)" appear only when there's something to act on.
8. **Words.**
   - Sentence case, verbs first: Join a team library, Start a team library, Leave team library, Copy to your
     prompts, Edit team library, Done.
   - Errors say what happened and what to do: "Can't reach that folder. Is OneDrive running?", "That isn't a Prompt
     Library team file."

## Additions after independent scoring (Claude's calls)

The independent projected score was below 95 %: requirements 92 %, build-readiness about 84 %, and screens 85–89 %.
These additions close each gap it named.

1. **Links.**
   - A link must return the **file itself**: a GitHub raw link, any web server, or a Dropbox link. The app rewrites
     Dropbox `dl=0` to `dl=1`, and GitHub `/blob/` links to raw.
   - If a link returns a web page, Join says: "That link opens a web page, not the file. Use the shared folder
     instead, or a direct download link." This happens with OneDrive and Google Drive share pages.
   - DEVELOPMENT.md and the Join dialog's help line say which links work.
2. **Join and Start are small modal dialogs** opened from Settings → **Team prompts** (the Settings heading uses
   the same name as the switch).
   - Join shows "Checking…" with a progress bar while it reads.
   - To change library you **Leave first**. While connected, Settings shows only the connected state, so there's
     no silent replacement.
3. **Write access** is found by a probe: open the file for writing without changing it. It runs at connect, on
   each refresh and on Retry.
   - Non-writers see a muted "Read only" in the Team header, where "Edit team library" would be, with the tooltip
     "You can't change files in this folder".
   - **Change detection uses the content hash** (SHA-256 of the file text).
   - **Conflict copies** are siblings named `team-prompts*.json` other than the file itself (OneDrive's
     `team-prompts-PCNAME.json`, Dropbox's `team-prompts (… conflicted copy …).json`).
4. **Restore and Review** use one dialog in the Restore window's style, opened from the Team header's **⋯** menu
   ("Restore an older version…").
   - Entries read "{date, time} · {n} prompts", newest first.
   - Conflict copies are listed first as "Other version from sync · {date, time} · {n} prompts".
   - Restoring makes a history copy of the current version first.
5. **Button rows by side and mode** (the left group is outline, the right group ends with the one filled button):

   | Where | Left | Right |
   |---|---|---|
   | Your prompts | Delete · Pin | Save · **Copy prompt** |
   | Team prompts (member) | Pin · Hide from menu | Make my copy · **Copy prompt** |
   | Team prompts (editing) | Delete · Pin | Save · **Copy prompt** |

   - "Copy to your prompts" is renamed **Make my copy**, and "Hide" is renamed **Hide from menu**. Pin becomes
     **Unpin** when pinned, and Hide from menu becomes **Unhide**.
   - The **Team header line** at the top of the Team side's middle column reads "{Name} · updated {when}", with
     at most two items on its right: **Edit team library** (or the muted "Read only"), plus **Mark all as seen**
     only while something is unseen, and the **⋯** menu.
   - **Show hidden ({n})** sits under the search box, only while something is hidden.
6. **States on the Team side:**
   - Loading on first read: "Loading team prompts…".
   - Empty: "No team prompts yet." Editors also see "Choose Edit team library to add some."
   - All hidden: "All team prompts are hidden." + Show hidden ({n}).
   - Offline: the header line in muted text with Retry.
   - Selection is **remembered per side**.
   - **Search across both:** the rail shows "All" while searching. Opening a result from the other library switches
     the side and keeps the search text.
7. **Feedback** appears on the status line for 4 s: "Pinned", "Unpinned", "Hidden from the menu", "Unhidden",
   "Saved for everyone", "Copied to your prompts · {Category}". Leaving edit mode with refreshes held says
   "Team prompts updated".
8. **Shortcut menu:**
   - When a team library is connected, small disabled headings **"Team prompts"** and **"Your prompts"** label the
     two blocks, which puts the word on screen as well as the icon.
   - With **more than 6 team categories**, the team block becomes one **"Team prompts ▸"** submenu at the same
     place, so a big team never pushes your prompts off the menu.
   - **Pins are capped at 10.** Pinning an 11th says "You can pin up to 10 prompts."
   - Same-named categories stay separate, and the team one carries the team icon.
9. **Setup guide:** a new **step 3 of 5** between Shortcut and Backups.
   - Heading "Does your team share prompts?". Body: "Connect to your team's shared prompts, or start a library for
     your team."
   - Two outline buttons, **Join a team library** and **Start a team library** (the same dialogs). **Next**
     continues without one, which is the default.

10. **Business examples (owner: "team prompt examples should be business orientated").**
    - **Start a team library** offers a checkbox, **"Add example business prompts"**, on by default when you don't
      copy any of your own categories in.
    - It seeds a ready-made, business-oriented best-practice set of **24 prompts in 6 categories, 4 each**. Each
      prompt follows the same pattern as the personal starter set: role, {placeholders}, the task and why, output
      format, and an invitation to ask clarifying questions.
      - **Sales:** discovery questions, follow-up email after a meeting, objection handling, proposal summary.
      - **Customer support:** empathetic reply to a complaint, troubleshooting steps, refund/escalation response,
        FAQ answer from policy.
      - **Marketing:** campaign brief, product launch announcement, case study draft, brand-voice rewrite.
      - **Meetings & management:** meeting agenda, minutes and action items, one-to-one prep, project status update.
      - **HR & people:** job description, interview questions with scoring guide, onboarding plan, policy summary
        for staff.
      - **Operations & finance:** SOP/process write-up, supplier comparison, budget variance explanation, risk
        register entry.
    - The set is written for a **generic company**: no real brands, no legal or financial advice. HR, finance and
      policy prompts carry a short "check with your HR / finance / legal team before acting" note.
    - The owner can edit or delete any of them.
    - Business examples are also used throughout: the **demo team library** for live tests and README screenshots
      ("Acme best practice"), the docs, and the rubric's team core tasks (e.g. copy the team prompt "Follow-up email
      after a meeting").

## Constraints

- Must follow `docs/design/ui-style.md` and pass the UI review rubric (≥ 90 %) for every new or changed screen
  (switch, Team side, read-only editor, edit banner, Settings section, setup step, menu).
- Existing behaviour for people with no team must not change, apart from pinning.
- Testing never touches the owner's real OneDrive or Google Drive: use `PROMPTLIBRARY_DEBUG_NO_CLOUDS=1` and a
  stand-in folder, as for backups.
- Upgrading from v1.0.x keeps every personal prompt, setting and backup.

## Left to the builder

- Exact XAML, icons and wording within the decisions and the app's tone. The UI rubric is the bar.
- Where hides and markers are stored (`settings.json` or a sidecar), the file-watcher debounce, and the URL size
  cap and timeouts.
- Whether the shared code (save with history, refresh) gets its own service class. It should: `AppHost` and
  `MainViewModel` are already large, and splitting them is a known deferral.

# UI review results

**Round 2, 2026-11-05.** I scored the running Debug build (commit 23f4210) against `ui-review-rubric.md`. Pass marks: UI ≥ 90% on every screen. Simplicity ≥ 95% for the main window and the setup wizard, ≥ 90% for dialogs. The full scoring, the screenshots and the fix details are in `.superpowers/sdd/2026-11-04-prompt-library/ui-review-2.md`.

- **Scaling tested:** 200% only (3840×2400 display). I didn't test 100% or 150% because that would mean changing a system setting. The app lays out in device-independent pixels, so I expect the same result at those scales, but that hasn't been checked.
- **Minimum window size:** 860×520, tested at 200%. Nothing is clipped.
- **How I tested:** the PC was locked for the whole review, so I didn't press any keys. I drove every screen through Windows UI Automation and window messages, and took screenshots without bringing windows to the front.

## Scores

| Screen | UI | Simplicity | Result |
|---|---|---|---|
| Main window: populated | 95.6 | 96.5 | Pass (round 3) |
| Main window: empty library | 93.8 | (main) | Pass |
| Main window: search with no results | 96.1 | (main) | Pass |
| Main window: search with a match | 95.5 | (main) | Pass |
| Main window: unsaved changes + error | 93.4 | (main) | Pass |
| Main window: minimum size | 94.9 | (main) | Pass |
| Main window: category Rename/Delete icons | 92.2 | (main) | Pass |
| Main window: "Saved" feedback | 95.0 | (main) | Pass |
| Main window: "Backups are off" bar | 95.6 | (main) | Pass |
| Main window: "Backups paused" bar | 93.3 | (main) | Pass |
| Save changes? | 95.3 | — | Pass |
| Delete prompt | 95.3 | — | Pass |
| Delete category (move prompts) | 94.4 | 96.5 | Pass |
| Restore confirm | 95.8 | — | Pass |
| Leave setup? | 94.4 | — | Pass |
| About | 94.4 | — | Pass (round 3) |
| Settings | 93.1 | 92.25 | Pass |
| Settings: waiting for new shortcut | 92.6 | (settings) | Pass |
| Settings: shortcut used by another app | 90.7 | (settings) | Pass (just) |
| Backup folder: nothing found | 93.8 | (settings) | Pass |
| Backup folder: backing up | 95.0 | (settings) | Pass |
| Backup folder: backed up | 94.9 | (settings) | Pass (round 3) |
| Backup folder: backup failed | 94.1 | (settings) | Pass |
| Settings: "Paste into the app I was using" | 95.8 | (settings) | Pass |
| Settings: "Restore from a backup…" link | 94.4 | (settings) | Pass |
| Restore: backup list | 94.4 | 92.0 | Pass |
| Restore: damaged backup | 96.1 | (restore) | Pass |
| Restore: nothing to restore | 93.8 | (restore) | Pass |
| Restore: safety copies | 92.8 | (restore) | Pass |
| Setup step 1: welcome | 95.6 | 96.75 | Pass (round 3 simplicity) |
| Setup step 2: shortcut, tried, in use | 95.2 / 95.8 / 95.2 | (setup) | Pass |
| Setup step 3: nothing found | 93.8 | (setup) | Pass |
| Setup step 3: folder chosen and backed up | 95.5 | (setup) | Pass (round 3) |
| Setup step 3: backing up | 95.0 | (setup) | Pass |
| Setup step 3: backup failed | 92.2 | (setup) | Pass |
| Setup step 4: ready | 94.0 | (setup) | Pass |
| Tray menu (criteria 1, 4, 8) | 92.9 | — | Pass |
| Tray messages (criterion 8) | 90.0 | — | Pass (just) |
| Installer: Welcome / Progress / Finish | 96.5 / 97.8 / 91.6 | — | Pass |
| Installer: Update welcome / Update finish | 96.5 / 91.6 | — | Pass |

**4 below target in round 2 (all pass in round 3, see below):**
- About
- The "backed up" message in Settings
- The same message in setup step 3
- Main-window simplicity (93.75 of 95)

Everything round 1 flagged has been fixed and works when tested live. That includes the editor being cut off at the minimum size, search not opening its results, the Yes/No message boxes, the blue colour, and the hidden category actions.

## Round 3 (after Task 18)

**2026-11-05, commit becebb6.** I re-scored the four screens that failed in round 2 and both simplicity scores, using the running Debug build (with `PROMPTLIBRARY_DEBUG_NO_CLOUDS=1`, at 200%, with the PC locked). I also spot-checked the populated main window and Settings for regressions. The full details are in `.superpowers/sdd/2026-11-04-prompt-library/ui-review-3.md`, and the screenshots are in `ui-review-3-shots/`.

| Screen | Round 2 | Round 3 | Result |
|---|---|---|---|
| About | 88.6 | 94.4 | Pass |
| Backup folder: backed up (Settings) | 87.8 | 94.9 | Pass |
| Setup step 3: folder chosen and backed up | 87.8 | 95.5 | Pass |
| Main window simplicity | 93.75 | 96.5 | Pass |
| Setup wizard simplicity | 95.0 | 96.75 | Pass |
| Main window: populated (spot check) | 94.4 | 95.6 | Pass |
| Settings (spot check) | 92.0 | 93.1 | Pass |

**Every screen now meets its target.** Checked live:
- The window opens on the first prompt, and clicking a category opens that category's first prompt. Before it does, it asks whether to save unsaved changes: Cancel keeps the edit, and Don't save moves on.
- The Ctrl+Tab hint shows only while you're typing in the prompt.
- Tab reaches Rename and Delete on the category row, and then moves on to "+ New category".
- The move dialog starts with nothing chosen.
- After a restore, "Library restored from …" shows in the window.
- The "Backed up to <folder>." line is short.
- The About dialog has been replaced.

**Found during the walkthrough (it was already there before Task 18):** if you close the main window with unsaved edits and open it again from the tray while "Save changes?" is showing, the app crashes and the edits are lost. The cause is `AppHost.ShowMainWindow` calling `Show()` while the window is still closing. This needs fixing.

**Smaller follow-ups:**
- Search only looks in the selected category.
- Clearing a search that found nothing leaves the editor empty.
- The custom folder's card is still called "Folder".
- The move list has no "Choose a category" placeholder.

## What round 2 asked to fix (now done)

1. **The "Backed up to…" message is too long.** It shows the full folder path, which wraps over two lines, and the ✓ ends up on a line by itself. Shorten the message to "Backed up." or the folder's name, and keep the ✓ beside the text.
2. **Leftover text after choosing a folder.** Once a folder is in use, setup and Settings still show "No cloud folder found…", Refresh and "Skip backups for now". Hide them.
3. **The About box is a plain message.** It should show the name as a heading and the version. Replace the raw `AppData` path with an "Open the folder" link.
4. **Main window simplicity:**
   - When you click a category, or open the app for the first time, open the first prompt.
   - Show "Ctrl+Tab inserts a tab" only while you're typing in the prompt.
   - Let the keyboard reach the Rename/Delete icons.
   - Don't pre-pick a category in the move dialog.
   - After a restore, say "Library restored" in the window, not only in the tray.
5. **Smaller wording fixes:**
   - Settings' "That shortcut is in use by another app." should suggest another shortcut.
   - The paused bar for a plain folder should ask "Is the drive connected?".
   - "Save changes to "Untitled prompt"?" should use the prompt's saved title.
   - In the restore list, the automatic copy made just before a restore should be labelled as such.

## Not checked live

- **Keyboard:** Tab order, focus rings while tabbing, the shortcut keys (Ctrl+N, Ctrl+S, Ctrl+F, Ctrl+Shift+C, F2, Delete, Esc, Enter) and typing a new shortcut. The PC was locked, so these were scored from the code.
- **Cloud folders:** on this PC the test build is set to hide them (a debug switch), so the OneDrive/Google Drive cards and the "Backing up…" state were scored from earlier screenshots (tasks 10 and 14).
- **Tray balloon messages:** scored from their text in the code.
- **Installer:** scored from the existing task 15 screenshots. I didn't install it again.
- **Scaling:** 100% and 150% weren't tested.

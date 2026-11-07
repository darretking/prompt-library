# Manual test checklist

Run on a real Windows PC with the Release exe (`publish\PromptLibrary.exe`) or the installed app. Tick each box as you go and note failures beside the item. Edge cases not repeated here are in [edge-cases.md](edge-cases.md); run that list too.

Tip: to start clean, exit the app and delete `%APPDATA%\PromptLibrary`. Release builds add a `PromptLibrary` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; remove it afterwards if you don't want startup.

## First run & setup
- [ ] Start with no `%APPDATA%\PromptLibrary`. Expected: the "Welcome to Prompt Library" wizard opens at step 1 of 5, and no tray balloon appears.
- [ ] A tray icon is present; hovering shows `Prompt Library (Ctrl+Alt+P)`. The library holds 39 starter prompts in 13 categories.
- [ ] Wizard step 2: press Ctrl+Alt+P. Expected: a check line confirms it. After the wizard closes, the same keys open the quick menu.
- [ ] Step 2: Change shortcut, press Ctrl+Alt+K. Expected: keycaps update and "Saved. Try it now..." appears. Esc in the box cancels only the capture; a second Esc skips the wizard.
- [ ] Step 2: choose a combination another app owns (start another hotkey tool first). Expected: "That shortcut is in use by another app." and it keeps listening.
- [ ] Step 2: Alt+F4 while listening closes the window. Enter moves through the steps.
- [ ] Step 3: Next without joining. Expected: no team switch afterwards. (Joining from here is in Team prompts below.)
- [ ] Step 4: pick a backup card, or Choose another folder with a scratch folder. Expected: it backs up and shows success. Keyboard focus lands on the selected card.
- [ ] Step 5: untick paste and finish. Expected: Settings shows paste off. Closing the wizard with x from any step skips it safely and it does not reappear on the next start.
- [ ] Leave the wizard early while the shortcut is taken by another app. Expected: the tray shows the "in use by another app" message once. With no conflict, no message.
- [ ] Settings, Change shortcut, press a combination another app owns. Expected: "That shortcut is in use by another app."; normal typing in other apps is unaffected.

## Shortcut & quick menu & auto-paste
- [ ] Notepad: press Ctrl+Alt+P, choose Coding, then Explain this code. Expected: the exact text is pasted, no "Copied" balloon, focus stays in Notepad.
- [ ] A browser text box (Chrome or Edge): same steps. Expected: pasted at the cursor.
- [ ] Windows Terminal: same steps. Expected: pasted as text, not run.
- [ ] VS Code and Claude desktop: same steps. Expected: pasted at the cursor.
- [ ] Claude Code (in a terminal): same steps. Expected: pasted into the prompt line.
- [ ] Menu navigation: arrow keys and Enter work; the Q&A category shows as `Q&A` with `Ask & answer` inside.
- [ ] With the menu open, click elsewhere (both via hotkey and via tray). Expected: the menu closes. Esc closes it and returns focus to the previous window.
- [ ] Hold Ctrl+Alt while picking a prompt. Expected: copy only, toast "Copied: ...", nothing pasted; Ctrl+V pastes it.
- [ ] Settings: turn off "Paste into the app I was using", pick a prompt. Expected: copy only, with toast `Copied: <title>. Press Ctrl+V to paste.`
- [ ] Open Notepad as administrator, click into it, press the hotkey, pick a prompt. Expected: the toast says Windows doesn't allow pasting into administrator apps, and the prompt is on the clipboard. (The menu may not open over admin windows: edge-cases.md #1.)
- [ ] Tray click path: click the tray icon, pick a prompt. Expected: copy only, with the toast.
- [ ] The toast header reads "Prompt Library", not the exe name. If an old name shows, run once from the final exe path and check again.
- [ ] Menu near screen edges: edge-cases.md #3.

## Main window
- [ ] Double-click the tray icon. Expected: the main window opens (the first click may flash the menu, which should close).
- [ ] Tray menu, Open Prompt Library shows the window. Closing it hides it to the tray; the first close shows the one-time "Still running..." notice, later closes don't.
- [ ] Tab and Shift+Tab move through every control with a visible focus ring. In the prompt box Tab leaves the box and Shift+Tab goes back; Ctrl+Tab inserts a tab (the hint under the box says so).
- [ ] New prompt, save with an empty title. Expected: validation message, nothing saved. A valid one shows "Saved".
- [ ] Select another prompt with unsaved edits. Expected: "Save changes to ...?" with Save, Don't save, Cancel.
- [ ] Search: typing selects the first match; the x clears it; no matches shows an empty message.
- [ ] Right-click a category (Rename, Delete); F2 renames; clicking away commits the rename. Delete on the prompt list asks first.
- [ ] Delete a category that has prompts. Expected: a dialog to move its prompts, then "Moved N prompts...".
- [ ] Delete every prompt. Expected: empty-library message; New prompt (Ctrl+N) puts focus in the title.
- [ ] Resize to the minimum width with a backup notice showing. Expected: nothing is cut off.

## Settings & backups
- [ ] Tray menu, Settings opens. Change shortcut: the listening box shows `Ctrl+Alt+...` while modifiers are held; Esc cancels; Tab leaves it.
- [ ] Backup folder: detected cloud folders show as cards plus Choose another folder; arrow keys move between cards and focus rings are visible.
- [ ] Pick a real OneDrive or Google Drive card. Expected: a note that files sync; Back up now writes a file there and shows `Backup saved.`
- [ ] Choose another folder through the folder dialog. Expected: it is used and backups appear there.
- [ ] Pause or disconnect the drive and press Back up now. Expected: a failure message naming the folder; the paused bar in the main window clears after reconnecting and re-activating the app.
- [ ] Leave the app running 1 to 2 hours. Expected: a scheduled backup appears only when the library changed.
- [ ] Back up to an offline network share, then Exit. Expected: Exit completes within about 10 s and the next start shows "The last backup didn't finish." once.
- [ ] "Start Prompt Library when Windows starts" toggles the Run registry value.
- [ ] Start with the backup folder missing or unreadable. Expected: a clear message, and the app still starts.

## Restore
- [ ] File, Restore lists backups with relative times. Double-click one. Expected: a confirmation, then the library is replaced and `Library restored from ...` appears.
- [ ] After a restore, a safety copy of the previous library is listed under safety copies, and nothing is written to the folder being browsed.
- [ ] Show file opens Explorer on the backup. Look in another folder works.
- [ ] Log off with unsaved edits in the window. Expected: no "Save changes?" prompt blocks the logoff.

## Tray & exit
- [ ] Tray Exit ends the process (check Task Manager) and the tray icon disappears at once; hovering over the tray shows no ghost icon.
- [ ] Log off or restart with the app running. Expected: no ghost icon after signing back in.
- [ ] Start a second copy. Expected: no second process; the first copy comes forward or stays as it is.
- [ ] Reboot (Release or installed build only). Expected: the app starts in the tray with no window, and the hotkey works.
- [ ] Remove the `PromptLibrary` Run value afterwards if you don't want startup.

## Updates

Build an older version first (`dotnet build src/PromptLibrary.App -p:Version=1.0.0`) and delete `%APPDATA%\PromptLibrary\update-check.json`.

- [ ] Start the app hidden and wait a minute. Expected: one notification "Prompt Library X is available. Click to update."; clicking it opens the update dialog.
- [ ] Not now. Expected: "Update available: vX" in the category rail and "Update to X…" at the top of the tray menu; both open the dialog. Restart: no second notification, the reminder is still there.
- [ ] Help → Check for updates… with the network off. Expected: "Couldn't check for updates…". With it on: the dialog, even after Not now.
- [ ] Update now (installed build, with unsaved edits open). Expected: download progress, then "Save changes?"; Cancel keeps the app running. Save: the app closes, the installer opens, and after it finishes the new version starts.
- [ ] Run the current version. Expected: no notification and no reminder.

## Team prompts

Use stand-in folders, never a real shared folder: run with `PROMPTLIBRARY_DEBUG_NO_CLOUDS=1` (Debug) and back up `%APPDATA%\PromptLibrary` first. Make folders `team-a` and `team-b` somewhere outside OneDrive / Google Drive / Dropbox.

**Solo user**
- [ ] Not joined. Expected: no Your/Team switch, no Team block or headings in the shortcut menu; Settings shows "Team prompts" with Join a team library and Start a team library; the editor shows Pin.

**Start and join**
- [ ] Settings → Start a team library: a folder that doesn't exist. Expected: "Can't reach that folder. Check the path, and that the drive or network is connected."
- [ ] Start in `team-a`, name "Acme best practice", "Add example business prompts" ticked. Expected: `team-a\team-prompts.json` is written, the switch appears, the Team side shows 24 prompts in 6 categories, Settings shows "Connected to Acme best practice", the folder and "Updated today, …".
- [ ] Start again in `team-a` from another PC or after leaving. Expected: "This folder already has a team library (Acme best practice)." with Join it; the file is not overwritten.
- [ ] Start in a folder whose `team-prompts.json` holds "not json". Expected: "This folder already has a team file that can't be read." and the file is not overwritten.
- [ ] Copy `team-prompts.json` to `team-b` and run `attrib +r team-prompts.json` there. Leave, then Join `team-b`. Expected: "Checking…" with a progress bar while it reads; the Team header shows "Read only" (no Edit team library, no ⋯); nothing is marked New on first join.
- [ ] Join a folder holding some other JSON file named `team-prompts.json`. Expected: "That isn't a Prompt Library team file." and nothing changes. Join `\nohost\share`. Expected: "Checking…", then "Can't reach that folder…".
- [ ] Join a OneDrive or Google Drive share link. Expected: "That link opens a web page, not the file…".

**Members**
- [ ] Ctrl+2 shows Team prompts and Ctrl+1 Your prompts. The open team prompt has the lock line "Team prompt · Acme best practice · read only"; title, description and prompt read as normal text, not greyed.
- [ ] Copy prompt on a team prompt (the button and Ctrl+Shift+C). Expected: copied, as for your own.
- [ ] Make my copy on "Follow-up email after a meeting". Expected: one click, the window switches to Your prompts with the copy open in a "Sales" category, status "Copied to your prompts · Sales".
- [ ] Pin two prompts (one yours, one the team's). Expected: "Pinned", a pin glyph on the rows, a flat Pinned block at the top of the shortcut menu with the team icon on the team one. Pin an 11th. Expected: "You can pin up to 10 prompts."
- [ ] Hide from menu on a team prompt. Expected: "Hidden from the menu", it leaves the list and the shortcut menu, "Show hidden (1)" appears under the search box; ticking it lists the prompt greyed; right-click → Unhide brings it back ("Unhidden").
- [ ] Right-click a team row. Expected: Pin / Unpin and Hide from menu / Unhide. On a hidden row Pin is disabled.
- [ ] Search "email" on either side. Expected: results from both, team ones with the team icon; opening a result from the other side switches sides and keeps the search text.
- [ ] Shortcut menu: "Team prompts" heading, the team categories with the team icon, then "Your prompts" and your categories. With 7 or more team categories the team block is one "Team prompts ▸" submenu.
- [ ] Another editor adds a prompt. Expected: within a few seconds a "New" tag and a dot on the Team switch; a changed prompt shows "Updated"; opening it or Mark all as seen clears them.

**Editors** (joined to `team-a`)
- [ ] Edit team library. Expected: the bar "Editing team library: changes go to everyone · Done", and Delete · Pin | Save · Copy prompt in the editor. Fix a typo and Save. Expected: "Saved for everyone", and a copy of the old file in `team-a\Prompt Library history\`.
- [ ] Delete a team prompt. Expected: "Delete "…"? This removes it for everyone." first.
- [ ] Mid-edit, change `team-prompts.json` in a text editor, then Save in the app. Expected: "Someone else changed the team library, so it's been reloaded. Your change is still here: check it and save again." Your typed text is still in the editor; Save again keeps both changes.
- [ ] Mid-edit, change `team-prompts.json` in a text editor, then delete a prompt in the app. Expected: "Someone else changed the team library, so it's been reloaded. Please make your change again." and the prompt is back.
- [ ] Type into a team prompt in edit mode, then Settings → Leave team library. Expected: "Save changes?" first; Cancel keeps the text and the team library.
- [ ] ⋯ → Restore an older version…. Expected: versions listed "Today, 08:36 · 24 prompts", newest first; Restore asks first, then "Restored for everyone".
- [ ] Copy `team-prompts.json` to `team-prompts-LAPTOP.json`. Expected: "Two versions of the team library found · Review". Review lists "Other version from sync · …" first. Keep current version (or Restore it). Expected: the notice clears and the copy moves into the history folder.
- [ ] Close the window while editing. Expected: edit mode ends; unsaved text gets the usual "Save changes?".

**Offline and leaving**
- [ ] Rename `team-a` and restart the app. Expected: the Team prompts still show; the header reads "Last updated … · can't reach the team library · Retry"; Settings shows the reason with Retry. Rename it back and press Retry. Expected: back to "Acme best practice · updated …". With "not json" in the file instead, the header reads "… · can't read the team library".
- [ ] Settings → Leave team library → Leave. Expected: the switch, Team block and pins on team prompts are gone; `%APPDATA%\PromptLibrary\team-cache.json` is deleted; your copies stay.
- [ ] Setup guide (Help → Setup guide…), step 3 of 5 "Does your team share prompts?": Join a team library and Start a team library open the same dialogs; Next skips; once joined it shows "Connected to …".
- [ ] Minimum window size (860×520) on the Team side, in edit mode and with a new prompt. Expected: nothing cut off (the window's minimum height grows while the switch and edit bar show).

## Scaling & accessibility
- [ ] At 100%, 150% and 200% display scaling (sign out and in after changing): main window, quick menu, Settings, Restore and the wizard are sharp, nothing is clipped, and the minimum window size still works. At 1920×1080 and 150 %, Settings fits the screen (it scrolls) with Close visible.
- [ ] Keyboard only: every screen is usable without the mouse and the focus ring is always visible.
- [ ] Windows dark mode and high contrast: text stays readable.
- [ ] Second monitor with different scaling: edge-cases.md #4.

## Edge cases
- [ ] Every case in [edge-cases.md](edge-cases.md) is marked Pass, Fail or N/A (elevated app, full-screen app, screen edges, mixed DPI, 200-prompt category, sleep and resume, clipboard history, Explorer restart, Remote Desktop, second Windows user, cloud folder paused, disk full, uninstall and reinstall).

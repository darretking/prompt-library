# Manual test checklist

Run on a real Windows PC with the Release exe (`publish\PromptLibrary.exe`) or the installed app. Tick each box as you go and note failures beside the item. Edge cases not repeated here are in [edge-cases.md](edge-cases.md); run that list too.

Tip: to start clean, exit the app and delete `%APPDATA%\PromptLibrary`. Release builds add a `PromptLibrary` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; remove it afterwards if you don't want startup.

## First run & setup
- [ ] Start with no `%APPDATA%\PromptLibrary`. Expected: the "Welcome to Prompt Library" wizard opens at step 1 of 4, and no tray balloon appears.
- [ ] A tray icon is present; hovering shows `Prompt Library (Ctrl+Alt+P)`. The library holds 12 sample prompts.
- [ ] Wizard step 2: press Ctrl+Alt+P. Expected: a check line confirms it. After the wizard closes, the same keys open the quick menu.
- [ ] Step 2: Change shortcut, press Ctrl+Alt+K. Expected: keycaps update and "Saved. Try it now..." appears. Esc in the box cancels only the capture; a second Esc skips the wizard.
- [ ] Step 2: choose a combination another app owns (start another hotkey tool first). Expected: "That shortcut is in use by another app." and it keeps listening.
- [ ] Step 2: Alt+F4 while listening closes the window. Enter moves through the steps.
- [ ] Step 3: pick a backup card, or Choose another folder with a scratch folder. Expected: it backs up and shows success. Keyboard focus lands on the selected card.
- [ ] Step 4: untick paste and finish. Expected: Settings shows paste off. Closing the wizard with x from any step skips it safely and it does not reappear on the next start.
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

## Scaling & accessibility
- [ ] At 100%, 150% and 200% display scaling (sign out and in after changing): main window, quick menu, Settings, Restore and the wizard are sharp, nothing is clipped, and the minimum window size still works.
- [ ] Keyboard only: every screen is usable without the mouse and the focus ring is always visible.
- [ ] Windows dark mode and high contrast: text stays readable.
- [ ] Second monitor with different scaling: edge-cases.md #4.

## Edge cases
- [ ] Every case in [edge-cases.md](edge-cases.md) is marked Pass, Fail or N/A (elevated app, full-screen app, screen edges, mixed DPI, 200-prompt category, sleep and resume, clipboard history, Explorer restart, Remote Desktop, second Windows user, cloud folder paused, disk full, uninstall and reinstall).

# Manual edge-case checklist

Run these during the UI review, on a real Windows PC with the installed app. The automated tests in
`tests/PromptLibrary.Core.Tests/EdgeCases/` cover the Core library; the cases below need a real desktop.

Mark each one **Pass**, **Fail** (with a note) or **N/A** (with the reason, e.g. no second monitor).

## Hotkey and quick menu

| # | Case | Steps | Expected |
|---|------|-------|----------|
| 1 | Elevated (admin) app has focus | Start Notepad with *Run as administrator*, click into it, press the hotkey. | Known Windows limit (UIPI): the menu may not open, or may not take focus, over an admin window. If a prompt is chosen, it is still copied, and Ctrl+V into the admin app works. Release notes say: "paste works, but the menu may not open over admin windows". The app doesn't crash or hang. |
| 2 | Full-screen app | Play a video full screen (browser F11) or a borderless full-screen game, press the hotkey. | The menu appears on top, or nothing happens. The app never freezes, and the full-screen app keeps working. |
| 3 | Menu near each screen edge | Put the mouse near the top, bottom, left and right edges and each corner, then press the hotkey. | The menu is completely on screen each time (it opens up or left when there's no room). |
| 4 | Second monitor with a different DPI | Set monitor 1 to 100% and monitor 2 to 150% scaling. Open the menu on each, then open the main window and drag it across. | Text is sharp and the right size on both monitors. The menu opens on the monitor the mouse is on. |
| 5 | Category with 200 prompts | Make a category with 200 prompts (import a test library), open the quick menu. | The submenu scrolls (arrows or scroll wheel). Every prompt can be reached and copied. |
| 6 | Sleep and resume | Press the hotkey, put the PC to sleep, wake it, press the hotkey again. | The hotkey still works without restarting the app. |
| 7 | Clipboard history | Turn on clipboard history (Win+V), copy a prompt from the menu, press Win+V. | The copied prompt is in the history with its line breaks intact. |

## Tray icon and sessions

| # | Case | Steps | Expected |
|---|------|-------|----------|
| 8 | Explorer restart | Task Manager → *Windows Explorer* → *Restart*. | The tray icon comes back within a few seconds and the hotkey still works. |
| 9 | Remote Desktop session | Connect to the PC over Remote Desktop, use the hotkey and the tray menu, then disconnect and reconnect. | Copy and paste work in the remote session. After reconnecting, the tray icon and hotkey still work. |
| 10 | Second Windows user on the same PC | Sign in as another user (fast user switching) while the first user's copy is running. | The second user gets their own copy running, with their own library in their own `%APPDATA%\PromptLibrary\`. The two users' libraries never mix, and the "single running copy" rule applies per user. |

## Backups and saving

| # | Case | Steps | Expected |
|---|------|-------|----------|
| 11 | Google Drive paused or signed out during a backup | Pause Google Drive sync (or quit it so `G:\` disappears), then press *Back up now*. Repeat with OneDrive paused or signed out. | When the folder can't be reached, a clear failure message names the folder. The app keeps running, the library is untouched, and the next backup works once the drive is back. (A paused but still mounted drive may accept the file and upload it later; that counts as a pass.) |
| 12 | Disk full on save | Point `%APPDATA%` at a small, nearly full drive (e.g. a 50 MB VHD), or fill the disk, then edit a prompt and save. | An error message, no crash. `library.json` still holds the previous version (no half-written file) and no `library.json.tmp` is left behind. |

## Install and uninstall

| # | Case | Steps | Expected |
|---|------|-------|----------|
| 13 | Uninstall and reinstall (MSI) | Add a prompt, uninstall, reinstall. | The library is still there (data in `%APPDATA%\PromptLibrary\` is kept). |
| 14 | Uninstall and reinstall (MSIX) | Add a prompt, make a backup to a cloud folder, uninstall, reinstall. | The library is gone (MSIX removes app data), but *Restore from backup* brings it back from the cloud folder. |

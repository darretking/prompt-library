# Most-used order and Pinned row — design

Date: 2026-11-14. Target version: 1.4.0. Owner delegated all build decisions; rulings below (U1–U12).

## Goal

Prompts the user reaches for most come first, everywhere a list of prompts is shown without a search query,
and the main window gets a Pinned row that gathers the pinned prompts in one place.

## Where things stand (1.3.0)

- `AppSettings.Usage` already records `Count` and `LastUsedAt` per prompt (`UsageTracker`).
- Tray menu (`QuickMenuBuilder`): Pinned (pin order) → Featured → Team categories → Your categories; categories and prompts A–Z.
- The shortcut opens the **search window** by default (`ShortcutOpens = "search"`). With no query it shows
  Pinned (pin order) → Featured → Recent (8) → All prompts (A–Z).
- Main window rail: All prompts → Recent → Featured (team side) → categories A–Z. No Pinned row.
- A use from the tray or search window does not rebuild the tray menu.

## Rulings

- **U1 One setting.** `AppSettings.PromptSort`: `"used"` (default) or `"name"`. Null or any other value means `"used"`.
  Settings window, new section **Prompt order**, radio buttons **Most used first** / **A to Z**.
- **U2 "Most used" = all-time use count** (`PromptUsage.Count`), not the 14-day frecency, so the order moves slowly
  and positions stay learnable. Ties, and prompts never used, keep A–Z (current culture, ignore case) after the used ones.
- **U3 Category score** = sum of the use counts of the prompts the category shows (hidden team prompts excluded). Same tie rule on the category name.
- **U4 Where the order applies** (when `PromptSort` is `"used"`):
  - Tray menu: the pinned list, team categories, your categories, and the prompts inside each.
  - Search window with no query: the Pinned group and the All prompts group.
  - Main window: the Pinned row's list, All prompts, and each category's list (when no search text).
- **U5 Unchanged:** Featured (editors' order), Recent (most recent first), any search with a query (best match first),
  the main window rail's category order and the category picker (A–Z — that's where categories are managed).
- **U6 With `"name"`** every place in U4 is A–Z, including pins (pin order is retired; nobody can see or change it today).
- **U7 Main window Pinned row.** Placed after All prompts, before Recent. Shown only when the side on screen has at
  least one pinned prompt that the list would show (same `IsListed` rule as Recent). Lists that side's pins in U4 order.
  Its tile shows the pin glyph `&#xE718;` (Segoe Fluent Icons, already used for the row pin marker) in `Brush.Brand`, like
  Featured's star. Not a category: no rename/delete; the open editor survives switching to it (as with Recent).
  Pinning/unpinning anywhere updates the row (via the existing `RefreshUserState`).
- **U8 Tray stays current.** After every recorded use (tray pick and search-window pick) the tray menu is rebuilt
  (`RefreshQuickMenu`), so the next open shows the new order. A rebuild while the menu is open is already deferred.
- **U9 Pin limit stays 10** (`TeamUserState.MaxPins`).
- **U10 Clear usage history.** Settings, under Prompt order: muted line "Order follows how often you use each prompt."
  and a button **Clear usage history…**. Confirm dialog (existing `ConfirmDialog`, window title stays "Prompt Library"): first line "Clear usage history?",
  then "Prompts go back to A to Z order until you use them again, and Recent is emptied.", buttons **Clear** / **Cancel**.
  On Clear: `Usage.Clear()`, save settings, rebuild tray, raise user-state-changed; status line "Usage history cleared."
  Button disabled when there is no usage.
- **U11 Changing Prompt order** saves at once (like every setting), rebuilds the tray and raises user-state-changed so
  an open main window re-sorts. The search window re-reads the setting each time it builds its list.
- **U12 Release.** Version 1.4.0, CHANGELOG entry, README feature line. Gallery renders the Pinned row and the new Settings section.

## Expected behaviour on edge inputs

- No usage at all (fresh install, or after Clear): every list is exactly today's A–Z order.
- Settings file from 1.3.0 (no `PromptSort`): behaves as `"used"`.
- Usage entry for a deleted prompt, or a pinned team prompt after leaving the team / hiding it: ignored, no crash, not shown.
- Same count, titles differing only in case: A–Z, stable.

## Out of scope

Manual drag ordering of pins or prompts; sort choice per list; syncing usage between PCs.

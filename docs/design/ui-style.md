# Prompt Library — UI style guide

Applies to every WPF window (main window, Settings, Restore, Move-category dialog) and to tray messages. The shortcut opens the search window by default; the classic native Windows menu remains an option.

## What the design is about

A prompt is a block of text you copy and paste into a terminal or chat box. The app's job is: find it, check it, copy it. The design spends its one bold move on the **prompt text itself** and on **Copy**; everything else stays quiet, like a well-made Windows utility.

## Colour tokens

Define once as `SolidColorBrush` resources in `Themes/Light.xaml` and `Themes/Dark.xaml` (same keys; `ThemeManager` swaps the active one into `App.xaml`). Views use `{DynamicResource Brush.*}` and never hard-code hex. The theme follows Windows unless Settings overrides it.

| Token | Light | Dark | Use |
|---|---|---|---|
| `Brush.Window` | `#F6F7F9` | `#1C1F26` | Window background, middle column |
| `Brush.Rail` | `#ECEFF3` | `#161920` | Left category rail |
| `Brush.Surface` | `#FFFFFF` | `#242833` | The editor card, fields, flyouts |
| `Brush.Ink` | `#1B2230` | `#E8EAF0` | Primary text |
| `Brush.Muted` | `#4F5868` | `#B2B9C6` | Descriptions, helper text, counts (≥ 5.5:1 on Window, Rail, Surface and the soft fills) |
| `Brush.Line` | `#D6DBE3` | `#353B48` | Field borders, the card's 1 px stroke (no column dividers) |
| `Brush.Brand` | `#4338CA` | `#8B85F0` | Selection indicator, focus ring, links, brand text. Matches the app icon (indigo bubble). |
| `Brush.BrandFill` | `#4338CA` | `#4338CA` | Copy button and ticked checkbox fill, with white text |
| `Brush.BrandSoft` | `#E8E7FB` | `#27264A` | Selected list item background |
| `Brush.Hover` | `#E8E7FB` at 50 % | `#27264A` at 50 % | List and rail row hover (BrandSoft at half opacity; text checked over Window, Rail and Surface) |
| `Brush.Accent` | `#F5A524` | `#F5A524` | Bookmark amber from the icon. Use sparingly: onboarding illustration only, never for text. |
| `Brush.Danger` | `#B42318` | `#F97066` | Delete confirmation text, error text |
| `Brush.DangerFill` | `#B42318` | `#B42318` | Filled delete button, with white text |
| `Brush.Warn` / text | `#FFF4D6` / `#6B4A00` | `#3A2F12` / `#F5D48A` | "Backups are off" bar |
| `Brush.WindowBackdrop`, `Brush.RailBackdrop` | = Window, Rail | = Window, Rail | The window and rail areas that show Mica; translucent tints of Window/Rail while Mica is active |
| `Brush.Shadow` | black at 8 % | black at 30 % | The editor card's shadow (colour and strength; not a text colour) |

All text pairs are checked at ≥ 4.5:1, and `Brush.Muted` at ≥ 5.5:1 on every layer, by `ThemeContrastTests`.

On Windows 11 every window gets a dark title bar (dark theme), rounded corners and a Mica backdrop (`Theming/WindowBackdrop`; dialogs use the lighter transient backdrop). Mica shows only through the Window and Rail areas; the editor card, its fields and the prompt slip stay opaque for readability. On Windows 10 these are skipped and the solid tokens apply.

No gradients. The only shadow in the window is the editor card's: a `DropShadowEffect` (blur 16, depth 2, `Brush.Shadow`), switched off when WPF reports low-tier rendering.

## Type

- **UI:** `Segoe UI Variable Text, Segoe UI` — native to Windows 11, crisp at every DPI.
  - 13 px regular body; 13 px semibold list titles; 12 px muted descriptions/counts; 18 px semibold for the editor's title field text; 12 px semibold field labels.
  - Sentence case everywhere. **No ALL-CAPS labels** (the mockup's "CATEGORIES" becomes "Categories").
- **Prompt text:** `Cascadia Mono, Consolas`, 14 px (AvalonEdit has no line-spacing setting, so the line height is the font's own). Monospace here is functional: what you see is exactly what gets pasted.

## Layout (rail, list, editor card)

```
┌────────────┬──────────────────┬──────────────────────────────────────┐
│ Categories+│ [Search       ][+]│ ╭──────────────────────────────────╮ │
│ All prompts│ ▌Explain this…   │ │      ● Save  Send to ▾ [Copy] …  │ │
│ ▌Coding   3│  Refactor for…   │ │ Title                            │ │
│  Testing   │  …               │ │ Description                      │ │
│  …         │                  │ │ [Coding ▾] tag tag               │ │
│            │                  │ │ Prompt             460 chars     │ │
│            │                  │ │ ╭ prompt slip ───────────────╮   │ │
│            │                  │ │ ╰────────────────────────────╯   │ │
│            │                  │ │ Updated 2 days ago · Used 5 times│ │
│            │                  │ ╰──────────────────────────────────╯ │
└────────────┴──────────────────┴──────────────────────────────────────┘
  200 px        200–260 px          * (min 420 px)
```

- Window default 1100×700, minimum 860×520. The rail is 200 px wide: long category names end in an ellipsis (the tooltip has the full name).
- **Layers and depth.** The rail and the list sit on the window layer (`Brush.RailBackdrop` / `Brush.WindowBackdrop`, so Mica shows through) with **no divider lines** between them: spacing and the rail's slightly different tone separate them. The editor is a **card** on top: `Brush.Surface`, 1 px `Brush.Line`, 8 px radius, 12 px from the window's right, top and bottom edges, 20 × 16 px inner padding, with the soft shadow. The prompt slip inside it is a `Brush.Window`-toned inset (1 px Line, 8 px radius). The card is as tall as its content: the slip is as tall as its text (at least about eight lines) and takes the remaining height only when the text is longer, then scrolls.
- **Header row** (top right of the card, no bottom bar): `Send to ▾` (subtle, split: the left part sends to the default AI app, the arrow opens the choices), `Copy prompt` (the one filled button, `BrandFill`, 32 px) and `…` (subtle, 32 × 32: Pin, Hide, Make my copy, Version history, Move to category, Delete prompt). The menus open under their button, right-aligned. **Save** is a subtle button with a Brand dot, present only while there are unsaved changes: it fades in and out over 83 ms; Ctrl+S always works, and a "Saved" toast confirms. Under the slip a quiet line: `Updated 2 days ago · Used 5 times` (from `UpdatedAt` and `AppSettings.Usage`), with `Ctrl+Tab inserts a tab` at its right while typing.
- **Rail.** Rows are 36 px (34 px plus a 1 px gap above and below), 13 px text, a 20 px emoji/icon column. The section header "Categories" is 12 px semibold `Muted` with a `+` icon button at its right (add a category). Counts are muted and show only on the selected or pointed-at row (All prompts always). Rename and delete icons show only while the row is pointed at or has the keyboard focus, never for selection alone.
- **List.** The search box has the **New prompt** icon button (`+`, tooltip "New prompt (Ctrl+N)") beside it; there is no footer button. Rows are two lines (title, description) with tag chips when they fit.
- **Selection** (rail and list): a rounded pill (4 px radius, 4 px inset from the column edges) filled `BrandSoft`, with a 3 × 16 px `Brand` indicator bar at its left, vertically centred (Windows 11 NavigationView). The indicator grows in over 83 ms. Hover = `Brush.Hover` on the same pill.
- 8 px grid for spacing, 32 px controls, left-aligned text, sentence case.

## The prompt and Copy

- Above the slip, the label row reads `Prompt` on the left and, on the right, a muted live count like `412 characters · 9 lines` and the **Edit | Preview** switch. The switch is only there when the prompt has `{fields}` or markdown (a heading, a list, **bold** or a fenced block): a plain prompt has nothing to preview. The preview's caption "Preview with your last-used values" shows only when the prompt has fields.
- **Copy** is the only filled button in the window (`Brand` background, white text, 32 px tall). Label: `Copy prompt`. After a click it shows `Copied` for 1.5 s, then returns. Shortcut Ctrl+Shift+C, shown in its tooltip.
- Delete lives in the `…` menu (text `Danger`).

## States

- **Empty library:** one centred card in the list column: a 48 px Fluent glyph, "Your library is empty", one line, `New prompt` (filled) and `Add starter prompts` (adds the 39 example prompts, skipping titles you already have). The editor column shows nothing (no hint), so the state is not said twice.
- **Nothing open:** the editor column shows a faint centred hint, "Select a prompt, or create one.", and no card.
- **No search match:** a glyph, `No prompts match "<query>"`, "Try fewer words, or search tags with #" and a `Clear search` link.
- **Unsaved:** window title gets a `•` prefix; the header shows Save (with a dot).
- **Validation:** the title's error is a `Danger` underline plus the message at the right end of the title's own row, so nothing moves; the prompt's error shares the line under the slip; never only a message box.
- **Backups off:** quiet `Warn` bar: "Backups are off." + `Set up backups` link. **Backups paused:** "Backups paused: <provider> isn't available. Is it signed in?" + `Set up backups`. Both clear automatically when resolved.

## Motion

Short and Fluent (enter curve cubic-bezier(0, 0, 0, 1)); every duration is zero when Windows' animation effects are off (`Views/Motion`, `SystemParameters.ClientAreaAnimation`).

- Dialogs (`AppDialog` style): fade and scale in from 96 % over 167 ms.
- Flyout menus, the menu bar's drop-downs and the combo box list: fade in over 83 ms.
- The selection indicator in the rail and the list grows in over 83 ms. Save fades in and out over 83 ms. Toasts fade over 167 ms.
- Nothing else animates.

## Keyboard and accessibility

- Visible 2 px `Brand` focus ring on every focusable control (replace default dotted focus).
- Tab order: rail → search → list → header buttons → Title → Description → Category → Prompt.
- Shortcuts: Ctrl+N new prompt, Ctrl+S save, Ctrl+F focus search, Ctrl+Shift+C copy, Delete on list = delete prompt (with confirm), F2 on rail = rename category.
- All controls have `AutomationProperties.Name`. Text contrast ≥ 4.5:1.
- Works at 100%, 150% and 200% display scaling with no clipped text.

## Words

Plain verbs, sentence case, the same name for an action everywhere: `Copy prompt` → tray message `Copied: <title>`; `Back up now` → `Backup saved.`; `Restore` → `Library restored from <date>.`. Errors say what happened and what to do: `Backup failed: G:\ isn't available. Is Google Drive running?`.

## Secondary windows

Settings, Restore and Move-category dialogs use the same tokens, 16 px padding, labels above fields, primary action bottom-right (filled `Brand` only if it's the dialog's main action), Cancel next to it. Fixed size, centred on owner.

## Round 2 additions (premium review)

- **Menu bar (File, Help):** flat and transparent over the window, 13 px Ink, headers 28 px with a 4 px radius and `Brush.Hover`; drop-downs are flyouts (Surface, 1 px Line, 8 px radius, 4 px padding, 32 px items, Line separators, muted shortcut text). The rail and prompt context menus use the same flyout.
- **Colour emoji:** category emoji are drawn in colour with Emoji.Wpf (`emoji:TextBlock`) in the rail, the category combo box, menus and the picker. Data stays plain emoji strings. The picker is a Surface flyout with 32 px cells (hover `Brush.Hover`) and a quiet "No emoji" text button.
- **Title and description:** a hairline Line underline at rest, a 1 px Line frame on hover, a 2 px Brand ring on focus; tooltip "Click to edit"; placeholders "Untitled prompt" and "Add a description".
- **Prompt slip:** a `Window`-toned inset with a 1 px Line frame and 8 px corners inside the editor card (the 4 px Brand left edge is gone); an empty box shows a muted hint.
- **Preview:** the result, not the source: remembered values as filled chips (Brand on BrandSoft), empty fields as muted outline chips with the field name, `{date}` / `{time}` today's values, `{clipboard}` a muted chip, `{cursor}` a thin Brand caret; headings 15 px semibold, **bold** semibold, bullets with a hanging indent, fenced code in a `CodeBlock` box. A muted caption above says "Preview with your last-used values" (only when the prompt has fields).
- **Version history:** the diff is the chosen version against the one saved just after it (the editor's text for the newest), titled "Changes made in this version"; "Compare with current" switches to the editor's text. "Restore as a new prompt" keeps the version as "<title> (restored)" in the same category and opens it.
- **Import results:** after an import a dialog shows imported / skipped duplicates / failed and, for failures, a scrollable `file — reason` list.
- **Delete category:** ticking "Also delete the N prompts" removes the "Move prompts to" label and box and shows "These prompts will be deleted. This can't be undone." in Danger text; unticked, the first other category is chosen and the helper says "Prompts move to the category you pick."

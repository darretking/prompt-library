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
| `Brush.Surface` | `#FFFFFF` | `#242833` | Editor fields, prompt slip |
| `Brush.Ink` | `#1B2230` | `#E8EAF0` | Primary text |
| `Brush.Muted` | `#5A6475` | `#A0A8B8` | Descriptions, helper text, counts (≥ 4.5:1 on Window, Rail and Surface) |
| `Brush.Line` | `#D6DBE3` | `#353B48` | Field borders, column dividers |
| `Brush.Brand` | `#4338CA` | `#8B85F0` | Selection edge, focus ring, links, brand text. Matches the app icon (indigo bubble). |
| `Brush.BrandFill` | `#4338CA` | `#4338CA` | Copy button and ticked checkbox fill, with white text |
| `Brush.BrandSoft` | `#E8E7FB` | `#27264A` | Selected list item background |
| `Brush.Hover` | `#E8E7FB` at 50 % | `#27264A` at 50 % | List and rail row hover (BrandSoft at half opacity; text checked over Window, Rail and Surface) |
| `Brush.Accent` | `#F5A524` | `#F5A524` | Bookmark amber from the icon. Use sparingly: onboarding illustration only, never for text. |
| `Brush.Danger` | `#B42318` | `#F97066` | Delete confirmation text, error text |
| `Brush.DangerFill` | `#B42318` | `#B42318` | Filled delete button, with white text |
| `Brush.Warn` / text | `#FFF4D6` / `#6B4A00` | `#3A2F12` / `#F5D48A` | "Backups are off" bar |
| `Brush.WindowBackdrop`, `Brush.RailBackdrop` | = Window, Rail | = Window, Rail | The window and rail areas that show Mica; translucent tints of Window/Rail while Mica is active |

All text pairs are checked at ≥ 4.5:1 by `ThemeContrastTests`.

On Windows 11 every window gets a dark title bar (dark theme), rounded corners and a Mica backdrop (`Theming/WindowBackdrop`; dialogs use the lighter transient backdrop). Mica shows only through the Window and Rail areas; editor fields and the prompt slip stay opaque for readability. On Windows 10 these are skipped and the solid tokens apply. Toasts may fade over 150 ms; nothing else animates.

No gradients. No drop shadows inside the window (Windows draws the window shadow).

## Type

- **UI:** `Segoe UI Variable Text, Segoe UI` — native to Windows 11, crisp at every DPI.
  - 13 px regular body; 13 px semibold list titles; 12 px muted descriptions/counts; 18 px semibold for the editor's title field text; 12 px semibold field labels.
  - Sentence case everywhere. **No ALL-CAPS labels** (the mockup's "CATEGORIES" becomes "Categories").
- **Prompt text:** `Cascadia Mono, Consolas`, 13 px, line height 20 px. Monospace here is functional: what you see is exactly what gets pasted.

## Layout (three columns, approved)

```
┌────────────┬──────────────────┬──────────────────────────────────────┐
│ Categories │ [Search prompts ]│ Title                                 │
│ All prompts│ Explain this code│ [Explain this code               ]   │
│  Coding  3 │  Plain-English…  │ Description                           │
│ ▌Testing 2 │ Refactor for…    │ [Plain-English walkthrough…      ]   │
│  …         │  …               │ Category  [Coding            ▾]       │
│            │                  │ Prompt                                │
│ + New      │ + New prompt     │ ┃ Explain what the following code…   │
│   category │                  │ ┃                                     │
│            │                  │ [Delete]          [Save] [■ Copy  ]   │
└────────────┴──────────────────┴──────────────────────────────────────┘
  200 px        260 px              * (min 420 px)
```

- Window default 1100×700, minimum 860×520. Remember size/position is out of scope. The rail is 200 px wide: long category names end in an ellipsis (the tooltip has the full name), and the rename/delete icons appear only on the selected, hovered or focused row.
- 16 px outer padding in the editor; 8 px grid for all spacing.
- Left-aligned text throughout. Counts right-aligned in the rail.
- **Selection:** selected category/prompt = `BrandSoft` background + 3 px `Brand` left edge. Hover = `Brush.Hover`, no animation. Rows are at least 40 px; the rail shows `emoji name` (20 px emoji column) and a Recent row under All prompts.

## The one bold move: the prompt slip

- The Prompt box is a white `Surface` slip with a 1 px `Line` border and a **4 px `Brand` left edge**, filling all remaining height in the editor.
- Above it, the label row reads `Prompt` on the left and, on the right, a muted live count like `412 characters · 9 lines`.
- **Copy** is the only filled button in the window (`Brand` background, white text, 32 px tall). Label: `Copy prompt`. After a click it shows `Copied` for 1.5 s, then returns. Shortcut Ctrl+Shift+C, shown in its tooltip.
- Save and Delete are outline buttons. Delete text is `Danger`. Save is disabled when nothing changed.

## States

- **Empty library:** middle column shows "No prompts yet" + a `New prompt` button; editor shows "Select a prompt, or create one." in muted text.
- **Empty search:** "No prompts match "<query>"." with a `Clear search` link.
- **Unsaved:** window title gets a `•` prefix; Save enabled.
- **Validation:** inline error text in `Danger` under the field (e.g. "Give the prompt a title."), never only a message box.
- **Backups off:** quiet `Warn` bar: "Backups are off." + `Set up backups` link. **Backups paused:** "Backups paused: <provider> isn't available. Is it signed in?" + `Set up backups`. Both clear automatically when resolved.

## Keyboard and accessibility

- Visible 2 px `Brand` focus ring on every focusable control (replace default dotted focus).
- Tab order: rail → search → list → Title → Description → Category → Prompt → buttons.
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
- **Prompt slip:** Surface page, 1 px Line frame, 8 px corners, 4 px Brand left edge; an empty box shows a muted hint.
- **Preview:** the result, not the source: remembered values as filled chips (Brand on BrandSoft), empty fields as muted outline chips with the field name, `{date}` / `{time}` today's values, `{clipboard}` a muted chip, `{cursor}` a thin Brand caret; headings 15 px semibold, **bold** semibold, bullets with a hanging indent, fenced code in a `CodeBlock` box. A muted caption above says "Preview with your last-used values".
- **Version history:** the diff is the chosen version against the one saved just after it (the editor's text for the newest), titled "Changes made in this version"; "Compare with current" switches to the editor's text. "Restore as a new prompt" keeps the version as "<title> (restored)" in the same category and opens it.
- **Import results:** after an import a dialog shows imported / skipped duplicates / failed and, for failures, a scrollable `file — reason` list.
- **Delete category:** ticking "Also delete the N prompts" removes the "Move prompts to" label and box and shows "These prompts will be deleted. This can't be undone." in Danger text; unticked, the first other category is chosen and the helper says "Prompts move to the category you pick."

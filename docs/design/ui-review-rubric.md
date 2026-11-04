# UI review rubric

Used for the final UI review of every window. Each screen is scored from screenshots taken from the running app (100% and 150% scaling) plus a keyboard walk-through. **Pass = 90% or more on every screen.** Below 90%, fix and re-score.

| # | Criterion | Weight | Full marks when |
|---|---|---|---|
| 1 | Matches spec & style guide | 15 | Layout, fields, buttons, copy match `docs/design/ui-style.md` and spec §6 |
| 2 | Visual hierarchy | 12 | Eye lands on prompt text and Copy first; one filled button per view |
| 3 | Consistency | 10 | Tokens only (no stray colours), 8 px spacing grid, consistent radius and control heights |
| 4 | Typography | 10 | Sentence case, no all-caps labels, scale per guide, monospace only for prompt text |
| 5 | Colour & contrast | 10 | All text ≥ 4.5:1; focus ring visible; selection clear |
| 6 | States | 12 | Empty, empty-search, unsaved, validation error, backups-off and disabled states all designed |
| 7 | Keyboard & accessibility | 12 | Full tab order, shortcuts work, visible focus, automation names |
| 8 | Words | 9 | Plain verbs, consistent action names, helpful errors |
| 9 | Scaling & resize | 10 | No clipping at 150%/200% or at minimum window size |

Score = Σ(weight × points/10). Record per-screen scores and the fixes made in `docs/design/ui-review-results.md`.

Screens: Main window (populated), Main window (empty library), Main window (search with no results), Main window (unsaved + validation error), Settings, Restore, Move-category dialog, Quick menu (native — scored only on criteria 1, 4, 8), tray messages (criterion 8 only).

---

# Simplicity & intuitiveness score

A second, separate score per screen. **Target: 95% or more** on the main window and the onboarding wizard; 90%+ on secondary dialogs.

Method: a cognitive walkthrough of the core tasks below as a first-time user who has never seen the app (no docs, no tooltips read first), counting clicks/keystrokes and noting every hesitation ("where do I…?", "what does this do?"), plus a 5-second first-glance test from the screenshot.

Core tasks (main window):
1. Copy the prompt "Write unit tests" to the clipboard.
2. Find a prompt that mentions "headline" without knowing its category.
3. Add a new prompt to Marketing and save it.
4. Fix a typo in an existing prompt's text and save.
5. Rename a category.
6. Delete a category that still has prompts.
7. Get back to the prompt you were editing after closing the window (tray).

| # | Criterion | Weight | Full marks when |
|---|---|---|---|
| S1 | First-glance clarity | 15 | In 5 seconds a newcomer can say what the window is for and where to start |
| S2 | Effort for core tasks | 15 | Tasks 1–4 each take ≤ 3 actions after the window is open; none needs a hidden gesture |
| S3 | Visual quietness | 10 | No redundant controls or text; every visible element earns its place; one primary action per view |
| S4 | Discoverability | 15 | Every core task's control is visible or one obvious step away (no right-click-only or shortcut-only paths for core tasks) |
| S5 | Plain words | 10 | Labels name what users want ("Copy prompt", "New prompt"), no jargon, consistent naming |
| S6 | Familiar patterns | 10 | Behaves like a normal Windows app (list/detail, Ctrl+S, Delete, Esc, menu locations) |
| S7 | Forgiveness | 10 | Hard-to-undo actions confirm; unsaved work is protected; errors say how to fix |
| S8 | Feedback | 10 | Every action shows it worked (copied, saved, deleted, moved) within 1 s |
| S9 | Low decision load | 5 | ≤ 1 decision per step; sensible defaults; no premature choices |

Score = Σ(weight × points/10). Record the walkthrough (steps, hesitations) alongside the score in `docs/design/ui-review-results.md`.

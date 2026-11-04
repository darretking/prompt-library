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

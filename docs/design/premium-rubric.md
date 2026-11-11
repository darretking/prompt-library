# Premium score rubric

Used to judge whether Prompt Library feels like a **premium** prompt library (benchmark: Raycast snippets, Alfred,
TextExpander, PowerToys Run, Linear-grade polish) rather than an average one. Scored by a reviewer who did **not** write
the code, from screenshots of the running app (sandbox data, light + dark, 100 % and 150 %) plus a code read.
**Pass = 90 % or more overall and no criterion below 6/10.** The existing `ui-review-rubric.md` scores still apply.

Each criterion is scored 0–10. Score = Σ(weight × points / 10).

Weights revised 2026-11-11 after web research (`premium-review-results.md` §Sources): evidence says premium in this
category is mostly speed, reach, reliability and trust; visual polish matters less.

## A. Functionality (55)

| # | Criterion | Weight | 10/10 when |
|---|---|---|---|
| F1 | Speed to prompt | 11 | Shortcut → typed 3–5 chars → Enter → pasted, < 1 s perceived, no mouse. Pinned/recent first with empty query. |
| F2 | Templates | 10 | `{placeholders}` detected, form is quick (Tab/Enter), remembers values, choice lists, live preview, dynamic tokens `{clipboard}` `{date}` `{time}` `{cursor}`, never mangles text. |
| F3 | Search quality | 7 | Typos, word order, partial words, tags and `cat:` work; best result is first; matches highlighted. |
| F4 | Organisation | 5 | Categories with emoji, tags, pins, recent; easy to move/rename/delete (incl. delete with prompts). |
| F5 | Safety | 5 | Version history with diff + restore; confirmations on destructive actions; nothing lost on crash. |
| F6 | Reach | 3 | Send to ChatGPT/Claude/Copilot/Perplexity works; default target per prompt; clipboard fallback. |
| F7 | Robustness & paste reliability | 8 | All tests green; no regressions; paste lands every time, clipboard restored afterwards (option), no stale clipboard. |
| F8 | Portability & trust | 6 | Export/import all prompts (JSON + Markdown), offline, no account, failures visible, no lock-in. |

## B. UI & feel (45)

| # | Criterion | Weight | 10/10 when |
|---|---|---|---|
| U1 | Visual craft | 7 | Mica/rounded corners, consistent radii and spacing, balanced density, crisp icons, no default-WPF look anywhere. |
| U2 | Dark mode | 5 | Follows Windows live; every window/state readable; no white flashes or stray light controls. |
| U3 | Hierarchy & calm | 7 | Prompt text and the primary action dominate; secondary actions tucked in overflow; no clutter. |
| U4 | Motion & feedback | 4 | Subtle (≤ 150 ms) fades, toast on copy/send/save, nothing jumps or flickers. |
| U5 | Search window | 10 | Feels like a launcher: instant focus, keyboard-only, clear selection, preview, footer hints, closes cleanly. |
| U6 | Editor | 4 | Highlighted placeholders/markdown, preview toggle, chip tags, in-place title; text round-trips exactly. |
| U7 | Accessibility | 4 | Contrast ≥ 4.5:1 both themes, focus visible, automation names, works at 150 %/200 %. |
| U8 | Footprint | 4 | Search window opens < 150 ms after the shortcut (pre-created, hidden); low idle memory; no lag typing. |

Record each round's per-criterion scores, evidence and fixes in `docs/design/premium-review-results.md`.

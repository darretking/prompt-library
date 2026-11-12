# Premium review results

Scores against `premium-rubric.md`, one section per review round.

## Sources (web research, 2026-11-11)

**Product criteria**: dev.to/navbuildz (best AI prompt managers 2026); raycast.com/blog/raycast-for-windows;
textexpander.com/ai-prompts; manual.raycast.com/dynamic-placeholders; manual.raycast.com/snippets;
promptbuilder.cc/blog/best-ai-prompt-manager-tools-2026; g2.com/products/textexpander/reviews; g2.com/products/aiprm/reviews;
macworld TextExpander 6 review; tidbits 2016 TextExpander pricing; hn.svelte.dev/item/48296678; makerstack PromptPaste review;
blog.ergonis.com best AI prompt tools 2026; windowslatest 2026-05-15 native UI. Findings: what matters most is instant
retrieval, working in every app, dynamic fill-ins, reliable insertion, no lock-in, local-first, stability, native feel and
fuzzy search. Visual polish has weaker evidence.

**Windows 11 UI**: learn.microsoft.com, design pages for typography, geometry, mica, acrylic, layering, color,
titlebar-design, iconography, timing-and-easing, page-transitions and keyboard-accessibility; dwmapi DWMWINDOWATTRIBUTE;
PowerToys Command Palette overview and settings; WPF what's new in .NET 9 and .NET 10; github lepoco/wpfui,
Kinnara/ModernWpf, iNKORE-NET/UI.WPF.Modern, Flow-Launcher. Recommendation: hand-rolled styles on our own tokens plus
DWM calls, because the built-in WPF Fluent ThemeMode is still incomplete or experimental in .NET 10.

## Round 1 (2026-11-11), independent reviewer: 78.7 %, FAIL

| ID | wt | pts | | ID | wt | pts |
|---|---|---|---|---|---|---|
| F1 | 11 | 9 | | U1 | 7 | 7 |
| F2 | 10 | 8 | | U2 | 5 | 8 |
| F3 | 7 | 8 | | U3 | 7 | 7 |
| F4 | 5 | 8 | | U4 | 4 | 6 |
| F5 | 5 | 8 | | U5 | 10 | 9 |
| F6 | 3 | 8 | | U6 | 4 | 7 |
| F7 | 8 | 7.5 | | U7 | 4 | 7 |
| F8 | 6 | 8 | | U8 | 4 | 8 |

Main gaps:
- Preview only restyles the text.
- Pasting relies on fixed delays, and nothing visible appears in the app after a paste.
- Speed isn't measured.
- The menu bar looks native.
- Emoji draw in one colour.
- The fill-in form is cramped.
- The title and description don't look editable.
- Rail labels get cut off.
- The delete-category dialog contradicts itself.
- Search-window placement uses the wrong monitor's DPI.

Ruling R15: the rubric's U4 limit of "≤ 150 ms" contradicted the spec and Microsoft's Fluent motion ramp (83/167 ms). It now follows the Fluent ramp.
Ruling R14: category emoji are drawn in colour with Emoji.Wpf (WTFPL licence), because single-colour glyphs look cheap.
Ruling R16: the File/Help menu bar stays, since it's a familiar Windows pattern, but it's restyled flat with theme tokens.

## Round 2 (2026-11-11), new independent reviewer: 77.2 %, FAIL

F1 8 · F2 8 · F3 8 · F4 9 · F5 9 · F6 8 · F7 7 · F8 8 · U1 7 · U2 8 · U3 7 · U4 6 · U5 8 · U6 8 · U7 7 · U8 7.
This reviewer was new and viewed only about 16 of 28 screens, so its calibration differs from round 1. From now on the same
reviewer is reused every round so scores are comparable. Its verdict: the search window meets launcher expectations; the
main window "looks like a good internal tool": flat, no depth, a huge empty editor, Save and "…" drifting along the bottom,
dim muted text, plain rail rows, generic empty states. Idle memory (~190 MB) is too high.

## Round 3 (2026-11-11), same reviewer as round 2: 84.0 %, FAIL (+6.8)

F1 9 · F2 9 · F3 8 · F4 9 · F5 9 · F6 8 · F7 8 · F8 8 · U1 8 · U2 8 · U3 9 · U4 7 · U5 9 · U6 8 · U7 8 · U8 7.
The reviewer viewed only 15 screens and read no code. Two of its fix items already exist: a per-prompt default send
target (Send menu → Set as default) and the "Couldn't paste" toast. Real bugs it found:
- the fill-in preview doesn't update while typing
- clipping at the minimum window size
- the empty-library card isn't centred
- dim dark-mode emoji

Ruling R18: integrate the File/Help menu into a custom Windows 11 title bar (as Notepad and Paint do), with native
caption buttons kept. It came up in three reviews.

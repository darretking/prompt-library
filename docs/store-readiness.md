# Microsoft Store readiness

Checked 2026-11-04 against Microsoft's current publishing docs. Short answer: **the app is a good fit for the Store, but it's not ready to submit as built.** It needs MSIX packaging and one code change (startup at login), plus some account and listing work only the owner can do.

## Route: MSIX through the Store (decided)

The Store accepts either an MSIX package or a traditional EXE/MSI installer. We go with **MSIX**:

| | MSIX via Store | EXE/MSI |
|---|---|---|
| Code signing | Store signs it for us (free) | Must buy a certificate from a trusted CA (~$200–400/yr); self-signed rejected |
| Hosting | Store hosts it | We host a versioned installer on our own HTTPS server/CDN |
| Updates | Automatic through the Store | Our job |
| Install/uninstall | Clean, sandboxed | Must support silent install |

WPF projects don't produce MSIX by default; Microsoft recommends wrapping them in an MSIX package for Store submission.

## What already meets the bar

- **Real, distinct value**: a prompt library with a global shortcut and native menu; not a web wrapper or template app.
- **No network, no account, no data collection**: prompts stay on the PC; backups go to a folder the user picks. This keeps privacy obligations minimal.
- **Full-trust desktop features** (tray icon, `RegisterHotKey`, clipboard, writing to the chosen backup folder) all work in a packaged desktop app.
- **App name** "Prompt Library" has no emoji or special characters (Store rule).
- **Icon** is original artwork (not the Vecteezy image), so there are no licensing problems.
- **Accessibility and stability** are covered by the UI review (keyboard, contrast, scaling) and the test suite; the Store's certification checks crashes and basic accessibility.

## Gaps to close (building work, Task 12)

1. **Start with Windows.** In an MSIX package, writing to the `HKCU\...\Run` key is silently virtualised and **ignored at login**. When running packaged, the app must instead declare a `windows.startupTask` in the package manifest and switch it on/off with the `StartupTask` API; keep the Run key only for the unpackaged exe. The Settings checkbox stays the same for the user.
2. **Package project.** Add MSIX packaging (manifest with identity, display name, logos generated from `app-icon.png`, `runFullTrust`, the startup task), build an `.msix`, and pass the Windows App Certification Kit locally.
3. **Data location.** Packaged apps get `%APPDATA%` redirected into the package's private folder, which is **deleted on uninstall**. That's acceptable because backups live in the user's Google Drive folder, but the first-run and Settings text should say so, and the Store description should mention backups.
4. **Single-file publish.** The `.exe` from Task 11 stays as the non-Store download; the MSIX is built from the same project.

## Owner-only steps (no code)

1. **Partner Center account.** Individual developer accounts are currently free; identity verification is required. Companies pay a fee.
2. **Reserve the name** "Prompt Library" in Partner Center as early as possible. If it's taken, fallbacks: "Prompt Library for Windows", "PromptShelf".
3. **Privacy policy URL.** Needed if an app collects personal information, and the listing asks for one anyway. Ours is short ("This app collects no data and makes no network connections; your prompts stay on your PC and in the backup folder you choose."), but it must be at a public URL. The GitHub repo is private, so host it as a small public page (an Artifact page or a public gist works).
4. **Listing:** description, at least one screenshot (the UI review produces these), category *Productivity* or *Developer tools*, age-rating questionnaire (expected: everyone), support contact.

## Sources

- [Submit your app — FAQ (Microsoft Learn)](https://learn.microsoft.com/windows/apps/publish/faq/submit-your-app)
- [Publish your first app (Microsoft Learn)](https://learn.microsoft.com/en-gb/windows/apps/package-and-deploy/publish-first-app)
- [Create an MSI/EXE app submission (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/create-app-submission)
- [Get started publishing (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/apps/publish/get-started)
- [Startup programs in MSIX (Advanced Installer)](https://www.advancedinstaller.com/startup-programs-msix.html)
- [Shipping a WPF app to the Store, and the autostart bug (DEV Community)](https://dev.to/phantasmat2018/shipping-a-wpf-side-project-to-winget-and-the-microsoft-store-and-the-autostart-bug-that-only-2oap)
- [Store Developer Code of Conduct (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/apps/publish/store-developer-code-of-conduct)

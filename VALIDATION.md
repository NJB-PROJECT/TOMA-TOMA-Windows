# Validation — Lyrivane Desktop 2.0.0

- Self-contained Release publish completed, with no compiler warnings/errors. Executable self-test: **75 checks, 0 failures**.
- Actual WPF demo window inspected through Windows Computer Use: active-line highlight, long lyric wrapping, expanded metadata search, source controls and reachable island settings. Demo uses original sample text and generated PCM; it is not a benchmark.
- Generated PNG/ICO embedded in WPF resources and executable. Installer banner/dialog refreshed. Native dark title bar inspected.
- Real Windows Installer upgrade from installed 1.6.2 to 2.0.0 completed with exit code **0**. Existing settings hash unchanged, new Lyrivane.exe present, previous TOMA TOMA.exe removed. UpgradeCode and storage/updater identifiers preserved. This run did not uninstall the user's application.
- UI feature clips and desktop image regenerated from actual WPF visuals in isolated showcase mode, with original sample text/generated PCM.
- No Android files are included or changed. No new latency/FPS measurement is claimed.

---

# Validation — Windows 1.6.2

- Final self-contained Release publish: 0 warnings/errors. Executable self-test: **75 checks, 0 failures**.
- Tests cover lyrics/LRC, real FFT/stereo, themes and update version comparison, official source validation, stable release filtering, offline cache policy, readonly cache, SHA-256/size verification, tampering and hostile redirects.
- MSI compiled/validated with WiX 4.0.6. A physical Windows Installer lifecycle test installed 1.6.1 into a non-default test folder, upgraded to 1.6.2, and uninstalled using only the product code. All exit codes were 0; installed EXE version was 1.6.2.0; uninstall removed the application files; the existing settings file hash was unchanged. Shortcuts were disabled for the isolated lifecycle test to avoid changing the tester's desktop; shortcut definitions were inspected in the MSI database.
- Initial automated errors 1619/1622 were caused by the test environment's virtual AppData path. Using the actual physical package file path resolved them. A custom-folder uninstall issue discovered during testing was fixed by persisting the installation directory in HKCU. No OS security policies or permissions were changed.
- Live GitHub integration on the preceding 1.6.1 installer: simulated 1.5.0 client detected the required update, downloaded the released MSI and verified its size/hash; current 1.6.1 client was allowed. The actual required-update window was inspected and exited without launching an installer. The updater implementation is retained in 1.6.2.
- Android is not part of this repository/release. Player/device coverage and actual FPS/latency remain limited. Showcase media uses original text/generated PCM, not benchmarks or private playback data.

## Earlier 1.5.0
59 self-tests passed; native theme preview/Cancel were checked. Feature clips export actual WPF components without recording the desktop.

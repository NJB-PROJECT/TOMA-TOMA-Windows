# Validation — Windows 1.6.1

- Self-contained Release publish: 0 warnings/errors.
- Final executable self-test: **75 checks, 0 failures**. Existing lyric/parser, stereo FFT and theme tests are retained. New updater tests cover numeric versions, stable-release filtering, canonical repository URLs, offline policy/cache, readonly cache, exact size/SHA-256 verification, cache reuse, hostile redirects and tampered/oversize downloads.
- MSI compiled with WiX 4.0.6/UI extension. Database opens successfully and reports the expected version, seven application/license files, per-user registry components, shortcut targets and upgrade metadata. The application extracted from the MSI matched the published payload hash in the packaging inspection.
- Automated MSI installation attempts in this environment returned Windows Installer 1622/1619. Interactive install/uninstall and a real major upgrade have **not** been confirmed by this automated test. A direct user install test was requested. No system security policies or permissions were changed to bypass these failures.
- No Android code or APK is part of this Windows source/release.
- Media/provider/device coverage and real-time FPS/latency remain limited. The existing showcase uses original text/generated PCM, not a performance benchmark.

## Earlier 1.5.0 validation

59 self-tests passed; native theme preview and Cancel behavior were checked in the running Windows application. Feature media exports actual WPF components without private desktop/playback data.

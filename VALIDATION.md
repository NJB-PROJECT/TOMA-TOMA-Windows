## 2.3.0 — provider backups and watermark removal

- Windows build: 0 warnings/errors; 123/123 offline self-tests passed. Added checks for Gemini quota → Groq fallback, missing-key skip, cancellation, free-route allowlist, and explicit Groq reset time.
- MSI 2.3.0 upgrade returned 0. Installed executable version/hash match the published payload; existing settings and encrypted Gemini key hashes were unchanged.
- Native WPF showcase regenerated and desktop layout inspected; no lyric credit watermark. Demo text/PCM only.
- Groq/OpenRouter live-account calls were not performed: user has no keys yet. Free-tier availability and generated pronunciation accuracy are not guaranteed.
- Android is maintained separately and is not part of this public Windows repository or release.

# Validation — Lyrivane Desktop 2.2.1

- Self-contained Release publish: no warnings/errors. **117 automated checks, 0 failures**.
- New deterministic tests verify Retry-After, fractional RetryInfo, daily reset in Pacific time, malformed error fallback, pacing with a fake clock, cancellation during debounce, cooldown persistence without plaintext keys, HTTP 429 blocking a second request, cached lyrics during cooldown, larger batches and chorus deduplication.
- A 150-line unique fixture used three requests, each at most 64 rows. A 100-line identical chorus fixture used one request containing one unique row. Legacy caches were promoted and reused for different metadata/timestamps without another request.
- Production generation has one in-flight request and a 20-second minimum start interval per key/model. A 3-second debounce avoids spending requests on rapidly skipped tracks. 429 state persists by a hashed key/model scope; temporary resumes are capped at two for the current song, daily quotas are not retried each minute.
- Quota tests use simulated responses and consume no real Gemini quota. This does not increase Google's project quota or guarantee that a depleted daily quota is restored. Existing original text/timestamps and encrypted key storage remain unchanged.
- Windows Installer upgrade 2.2.0 → 2.2.1 completed with exit code 0; settings and encrypted-key hashes unchanged; installed EXE version 2.2.1.0 matches the payload hash.
- No Android changes, credentials or private pronunciation/rate caches are part of the release.

---

# Validation — Lyrivane Desktop 2.2.0

- Self-contained Release publish completed without warnings/errors. **105 automated checks, 0 failures**.
- New checks cover Gemini model/endpoint validation, row ID mapping and missing/duplicate/foreign IDs, ASCII pronunciation format, request-key placement, cache reuse, preserved timestamps/text, cancellation, sanitized provider errors, DPAPI round-trip and default cloud opt-out.
- Live Google AI Studio integration tested with the user's own locally entered key (never exposed to tools/logs). `gemini-3.5-flash` initially returned HTTP 503. `gemini-3.5-flash-lite` subsequently produced valid structured pronunciation rows for Vietnamese, Japanese, Arabic and Russian sample text. Earlier unrecognised Japanese sample result was explicitly unsupported; local dictionary fallback is retained. No claim of universal language/dialect accuracy is made.
- Gemini settings allow account model discovery, hidden key entry, encrypted per-user key storage and a Vietnamese sample test. Only safe result/error metadata appears in diagnostics. Sample text is original/common greeting material, not commercial song lyrics.
- Unsupported AI rows retain local readings and are not cached as completed pronunciation; a subsequent retry is tested. AI status is cleared on song changes so missing lyrics do not display a previous song's completion state.
- Existing original lyrics, timings and offline display remain available during generation and failures. Expanded island labels the current source/failure and provides retry. AI is OFF for existing users until enabled with their own key; this tester opted in directly.
- Real MSI upgrade from installed 2.1.0 to 2.2.0 completed with exit code 0. Existing settings and encrypted key file hashes were unchanged; installed EXE and privacy documentation match the release payload.
- No Android files or private credentials/cache are included in this repository/release.

---

# Validation — Lyrivane Desktop 2.1.0

- Release self-contained publish: zero warnings/errors. Executable **89 checks, 0 failures**.
- Native offline conversion tested using packaged EXE: Japanese kanji/kana (including Japanese-only-kanji context), Arabic with and without vowel marks, Korean, Russian, Hindi and Chinese. Original Latin is not duplicated. Mixed Japanese/Arabic/Korean batches work. Input size and cancellation are bounded.
- Original text/timestamps remain unchanged. Latin annotation runs on a background task with a bounded cache; stale/cancelled work cannot replace new song lyrics. Missing readings remain explicitly unavailable.
- Actual Windows UI checked in isolated demo mode: original+Latin rows, checkbox off/on, expanded island original+Latin, wrapping. Sample text is original/non-song material. Preview settings are not saved to the user profile.
- Arabic without harakat can lack vowels; text conversion does not restore missing vowels, translate lyrics, or analyze the recording. Japanese dictionary readings may differ from names/slang or a singer's intended pronunciation. No new timing is fabricated for untimed lyrics.
- Uses Apache Lucene.NET Kuromoji dictionary/resources embedded in the EXE and the OS ICU C API. Full dependency/dictionary notices ship with the installer. No NMeCab runtime is included.
- Real per-user MSI upgrade from 2.0.0 to 2.1.0: exit code 0, settings hash unchanged before launch, installed EXE version 2.1.0.0 and SHA-256 matching the published payload. Installed full dependency/dictionary notice hash matches the repository.
- No Android changes are part of this release.

---

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

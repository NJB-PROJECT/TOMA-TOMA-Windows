# Changelog

## 2.2.1 — 2026-11-12
- Serialize and pace Gemini generation calls (20-second minimum interval, 3-second song stability debounce).
- Increase batches to 64 rows with a character cap, deduplicate identical chorus lines and reuse identical lyric documents across metadata/timing changes.
- Parse Retry-After/RetryInfo and daily quota identifiers; persist 429 cooldowns, distinguish daily reset from temporary limits, and bound automatic resumption.
- Cache/local lyrics remain usable during cooldown; add an AI Studio quota link.


## 2.2.0 — 2026-11-12
- Optional Google AI Studio Gemini pronunciation in Indonesian-style phonetic spelling, including Latin-script languages such as Vietnamese.
- User-owned DPAPI-encrypted key, model discovery, sample test, explicit cloud opt-in, local result cache and retry.
- Active-line-first batches with strict row ID validation, cancellation, bounded requests and offline fallback; original lyrics/timing preserved.
- Vietnamese no longer silently skipped as already-readable Latin.


## 2.1.0 — 2026-11-11
- Offline automatic Latin reading annotations: Japanese kanji/kana via Kuromoji; Arabic, Korean and other supported scripts via Windows ICU.
- Original text and timestamps preserved; Latin under original in the desktop/expanded island and preferred in the compact island.
- Persistent display toggle, background processing, bounded cache and cancellation on lyric/song changes.
- Explicit approximation/Arabic missing-vowel notices; no translation or fabricated synchronization.


## 2.0.0 — 2026-11-11
- Rebrand to Lyrivane Desktop with generated mint/cyan ribbon logo and installer artwork.
- Rebuilt listening workspace: focused lyric panel, separate transport card, progressive search/settings and dark scrollbars.
- Stable lyric row typography, dark native title bar, Space/Ctrl+O/Ctrl+L shortcuts.
- Preserve MSI identity, settings and updater compatibility for 1.6.x migration.


## 1.6.2 — 2026-11-08
- Remember the MSI installation directory in HKCU so upgrades and uninstall correctly locate custom-folder installations.
- Verified old-version install, upgrade, cleanup and preservation of existing settings.

## 1.6.1 — 2026-11-08
- Per-user Windows MSI installer with desktop/Start Menu shortcuts, uninstall and major-upgrade identity.
- Mandatory updates when a valid newer stable release is detected, on startup and periodic checks.
- GitHub-only downloads with bounded redirects, exact size and SHA-256 verification; cached version requirement works offline.
- Preserve settings/lyric cache across installs; direct MSI download link for users.
- Existing 1.5.x installations need a one-time manual MSI migration.

## 1.5.0 — 2026-11-07
- Independent compact/expanded island themes with live preview, Save/Cancel and four presets.
- Solid, gradient, or local-image backgrounds; text/accent/border colors and background opacity.
- Robust fallback for missing images or invalid colors; safe bounded image decoding.
- Open-source Windows distribution, MIT license, contributor/privacy docs and reproducible feature showcase.

## 1.4.2
- Real stereo L/R spectrum and separate vertical Bass/Sub-Bass frequency bands.

## 1.3.0
- Improved synchronized lyric search, null-duration handling, free-text fallback and bounded server retry.

Earlier builds introduced playback sessions, LRC import, wrap-aware island layout and animated expand/collapse.

# Changelog

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

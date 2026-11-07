# Data & privacy

- No account, API key, telemetry, analytics, or automatic cloud backup is built into TOMA TOMA.
- Track title, artist and duration are sent to `https://lrclib.net` for lyric lookup. The fallback provider `https://api.lyrics.ovh` receives artist/title. Their independent privacy policies and availability apply.
- Audio spectrum uses the Windows default output loopback. Audio samples stay in memory; the app does not write or upload audio recordings. It does not capture the microphone.
- Album artwork comes from the current Windows media session. Background images are read from local files only.
- Settings, lyric cache, per-song timing and local image paths are stored under `%LOCALAPPDATA%/TomaTomaWindows/settings.json`. Diagnostic `app.log` stays beside it and is size-limited. These files are not included in the repository or release ZIP.
- Opening a music link launches the external browser/player, which has its own data practices.
- `--probe` and `--lyrics-probe` create local diagnostic files only when explicitly invoked. They can contain track metadata. Review before sharing. The app never automatically submits logs.
- Public feature clips use original example text and generated PCM in isolated showcase mode. They contain no personal playback history.

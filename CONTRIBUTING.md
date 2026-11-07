# Contributing

Use Windows and .NET SDK 8. Build with `./build.ps1 -Publish`, then run `./dist/TOMA TOMA.exe --self-test <report.json>`.

For changes to the island, check compact/expanded states, long wrapped lines, monitor positioning, click-outside behavior, swipe collapse, and theme Save/Cancel. For audio changes, verify known frequency tones, one-sided stereo and silence. Do not replace real FFT output with random animation. Keep plain lyrics clearly distinguished from timestamped lyrics.

Keep tests focused on behavior. Include steps to reproduce UI bugs, the app version and player type; redact personal metadata from logs. Never commit tokens, settings.json, lyric caches, recordings, proprietary lyrics, or build/SDK folders.

The Windows repository is intentionally separate from the Android project. Contributions and assets must be compatible with the MIT license; document third-party attribution.

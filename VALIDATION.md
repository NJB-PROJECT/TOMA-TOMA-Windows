# Validation — Windows 1.5.0

Release validation is recorded separately from product claims.

- Release build and self-contained publish: completed without warnings/errors.
- EXE self-test: **59 checks, 0 failures** (LRC parsing/timing, matching/query fallback, PCM/FFT and stereo isolation, low frequency bands, long wrapped capsule lyrics, theme isolation/copy/serialization/fallback).
- Feature clips export actual WPF visuals from showcase mode with original sample text and generated PCM. They are not performance benchmarks.
- HyperIsland on Android is outside this repository. This release contains Windows source/artifacts only.
- Real-time FPS/latency and every third-party player/audio device combination are not exhaustively measured. Availability of synchronized lyrics is provider-dependent.

Native UI: theme dialog, Aurora preset preview, and Cancel restoration were checked in the actual Windows application. The published GIF/MP4 media contain only isolated showcase visuals.

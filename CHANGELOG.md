# Changelog

All notable changes to LiveAudioBoard are documented in this file. The project follows
[Semantic Versioning](https://semver.org/) for public releases.

## [Unreleased]

### Fixed

- Prevented underlying library content from bleeding through the recording modal.
- Corrected playback-route, recording-source, and export-format option labels in custom combo boxes.
- Removed clipping from audio cards and the persistent player at Windows display scaling above 100%.
- Replaced native scroll bars and refined range sliders to match the glassmorphism interface.

## [0.22.0] - 2026-08-11

### Added

- Glassmorphism WPF desktop interface and layered .NET 10 architecture.
- SQLite audio library with file/folder import, recursive scanning, drag-and-drop organization,
  categories, favorites, search, paging, stable card ordering, SHA-256 deduplication, and backups.
- Multi-sound NAudio/WASAPI playback with live and monitor buses, per-sound routing, hotkeys,
  looping, exclusive mode, fades, trim ranges, playback cooldown, and emergency stop.
- Output-device hot-plug recovery and automatic fallback when Windows defaults change.
- EBU R128-style LUFS analysis, safe suggested gain, per-sound peak protection, batch analysis,
  final `-1 dBFS` bus limiting, and real-time level feedback.
- Microphone and Windows loopback recording with duration limits, input metering, silence trimming,
  48 kHz stereo conversion, and automatic import.
- Non-destructive WAV, MP3, and M4A rendering that preserves the original file.
- Openverse discovery for Freesound, Jamendo, and Wikimedia Commons.
- RSS 2.0, Media RSS, and Atom audio attachment browsing.
- Freesound OAuth2 authorization, DPAPI-encrypted credentials, token refresh, and original-file
  downloads.
- Internet Archive search restricted to explicitly marked CC0, public-domain, or CC BY items.
- Three-lane background download queue, per-item cancellation, duplicate prevention, safe HTTP range
  resume, attribution metadata, and automatic library import.
- Rotating crash diagnostics, missing-media recovery, and content-verified relocation.
- Velopack per-user Setup, MSI, portable package, GitHub Release workflow, optional signing, and
  in-app update checks.

### Security and compliance

- Freesound credentials are encrypted with Windows DPAPI and excluded from Git.
- Download URLs reject unsupported schemes and downloaded files remain subject to source licensing.
- Runtime dependencies are scanned for known NuGet vulnerabilities in release verification.
- Project licensing standardized on PolyForm Noncommercial 1.0.0 with separate commercial licensing.

[Unreleased]: https://github.com/2683445453/live-audio-board/compare/v0.22.0...HEAD
[0.22.0]: https://github.com/2683445453/live-audio-board/releases/tag/v0.22.0

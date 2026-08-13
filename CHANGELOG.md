# Changelog

All notable changes to LiveAudioBoard are documented in this file. The project follows
[Semantic Versioning](https://semver.org/) for public releases.

## [Unreleased]

No unreleased changes.

## [0.22.3] - 2026-08-13

### Added

- Persistent user-created categories, including empty categories, with an inline library creator.
- Persistent master and per-audio hotkey switches plus optional pass-through to the foreground app.
- A safe `--migrate-user-data` recovery command for migration without opening the interface.

### Fixed

- Moved the database, managed media, downloads, recordings, settings, backups, credentials, and logs
  out of the Velopack install root into `%LOCALAPPDATA%\LiveAudioBoard.UserData`.
- Added staged, non-destructive migration of recognized legacy data and automatic remapping of managed
  media paths so existing downloads survive reinstall and update cleanup.
- Prevented playback summary text inside audio cards from clipping at scaled Windows display sizes.
- Made the OBS guidance area scroll within its own row so it can no longer overlap the emergency stop
  button.

## [0.22.2] - 2026-08-13

### Added

- Editable per-audio categories for imported, downloaded, recorded, and rendered library items.
- Non-destructive waveform trimming with draggable start/end handles and movable selections.

### Fixed

- Prevented underlying library content from bleeding through the recording modal.
- Corrected playback-route, recording-source, and export-format option labels in custom combo boxes.
- Removed clipping from audio cards and the persistent player at Windows display scaling above 100%.
- Replaced native scroll bars and refined range sliders to match the glassmorphism interface.

### Release

- Aligned project, build-script, workflow, and changelog versions so tagged packages pass release
  metadata validation.

## [0.22.1] - 2026-08-12

- No installable artifacts were produced because release validation detected that the project was
  still declared as `0.22.0`. This empty release is superseded by `0.22.2`.

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

[Unreleased]: https://github.com/2683445453/live-audio-board/compare/v0.22.3...HEAD
[0.22.3]: https://github.com/2683445453/live-audio-board/compare/v0.22.2...v0.22.3
[0.22.2]: https://github.com/2683445453/live-audio-board/compare/v0.22.1...v0.22.2
[0.22.1]: https://github.com/2683445453/live-audio-board/releases/tag/v0.22.1
[0.22.0]: https://github.com/2683445453/live-audio-board/releases/tag/v0.22.0

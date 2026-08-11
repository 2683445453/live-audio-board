# Contributing to LiveAudioBoard

Bug reports, reproducible test cases, documentation corrections and feature discussions are welcome.
Because the project reserves commercial licensing rights, code contributions require prior agreement
with the repository owner before a pull request is merged.

## Before contributing code

1. Open an issue describing the problem, intended behavior and proposed scope.
2. Wait for the repository owner to confirm the design and contributor-license requirements.
3. Do not submit code copied from incompatible or unidentified sources.

Submitting a pull request does not by itself guarantee acceptance. A separate contributor agreement may
be required so the project can continue offering both noncommercial and commercial licenses.

## Development workflow

- Create features from `main` using `feat/<description>` and fixes using `fix/<description>`.
- Use focused commits with `feat:`, `fix:`, `docs:`, `test:` or `chore:` prefixes.
- Preserve the existing layered architecture and glassmorphism UI rules.
- Never commit user media, databases, logs, API credentials, signing certificates or generated packages.

Before requesting review, run:

```powershell
dotnet restore LiveAudioBoard.sln
dotnet format LiveAudioBoard.sln --verify-no-changes --no-restore
dotnet build LiveAudioBoard.sln --configuration Release --no-restore
dotnet test LiveAudioBoard.sln --configuration Release --no-build --no-restore
dotnet list LiveAudioBoard.sln package --vulnerable --include-transitive
```

For release-related changes, also run `./scripts/verify-release-metadata.ps1` and follow
[docs/RELEASING.md](docs/RELEASING.md).

## Licensing and media

Accepted source code is distributed under the repository's PolyForm Noncommercial 1.0.0 terms unless a
separate written agreement says otherwise. Third-party audio must not be committed to the repository
without documented permission and attribution. Software licensing never grants rights to audio content.

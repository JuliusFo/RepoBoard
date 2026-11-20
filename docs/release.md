# CI und Release

Vor Änderungen an `.github/workflows/`, an der Version oder vor einem Release lesen. Entscheidungen: ADR 0004.

## Versionierung

- SemVer, Version in `Directory.Build.props`, `CHANGELOG.md` pflegen (Abschnitt `## [X.Y.Z] - Datum`), Unterabschnitte in der Reihenfolge `Security`, `Added`, `Changed`, `Fixed`, `Internal`.
- Mit dem CHANGELOG den Release-Stand der Pakete setzen: Spalte „Release“ im Index `docs/work-packages/README.md`, „noch ohne Release“ im Kopf jedes Protokolls.
- Git-Tag `vX.Y.Z` je Release, erst wenn der CHANGELOG-Abschnitt steht. Nicht pushen und nicht taggen ohne Rückfrage beim Nutzer.

## CI (`.github/workflows/ci.yml`)

- **Linux:** Core und UI bauen (`-warnaserror`, ohne Windows-APIs), Core- und UI-Tests mit Coverage (Einstellungen `tests/coverage.config`, Bericht per ReportGenerator als Artefakt und Zusammenfassung). Die Git-Tests laufen dort gegen echte Repos (git ist auf den Runnern installiert); fehlt git in der CI, scheitern sie statt zu überspringen.
- **Zeilenenden:** eigener Job `line-endings`; beheben mit `git add --renormalize <Datei>` (ein einzelnes CR muss vorher in der Datei selbst raus).
- **Windows:** ganze Solution mit `-warnaserror`, alle Tests.
- **Concurrency:** PR-Läufe nach PR-Nummer gruppiert, nie nur nach `github.ref`.
- **Lokal wie der Linux-Job:** `pwsh -NoProfile -File tools/linux/Test-Linux.ps1` kopiert den Arbeitsstand in einen Container mit .NET-10-SDK (mit git), baut Core und UI und führt die Tests aus; ins Repo schreibt er nichts. Vorher prüft er lesend auf CRLF im Index.
- **Pflicht-Checks (Entscheidung des Nutzers, 2026-11-20):** Das Ruleset „Protect main“ (wie in FerretSharp) verlangt für einen Merge die Jobs „Core, UI and tests (Linux)“, „Solution (Windows)“ und „Line endings“ (nicht aktuell mit `main` nötig) und verbietet Löschen und Force-Push von `main`. Repo-Admins stehen auf der Bypass-Liste: Doku-Commits gehen direkt auf `main`. Ein umbenannter oder neuer Job muss im Ruleset mitgeändert werden, sonst wartet jeder PR auf einen Check, der nie kommt.
- Das Repo ist dafür öffentlich (Entscheidung des Nutzers, 2026-11-20): Auf dem kostenlosen Plan gibt es Rulesets nur für öffentliche Repos.

## Distribution

Noch keine: `dotnet run --project src/RepoBoard.App` bzw. `dotnet publish src/RepoBoard.App -c Release -r win-x64 --self-contained -o artifacts/publish`. Installer und Updates (Velopack): Roadmap.

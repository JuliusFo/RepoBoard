# ADR 0004: Versionierung wie FerretSharp

- Status: akzeptiert (mit dem Aufbau aus FerretSharp, Entscheidung des Nutzers 2026-11-20)
- Datum: 2026-11-20

## Entscheidung

- SemVer, Version nur in `Directory.Build.props`, Start mit **0.1.0** (vor 1.0 darf sich alles ändern).
- `CHANGELOG.md` nach Keep a Changelog, Abschnitte in der Reihenfolge Security, Added, Changed, Fixed, Internal.
- Tag `vX.Y.Z` je Release, erst wenn der CHANGELOG-Abschnitt steht, und nur nach Rückfrage beim Nutzer (`/release`).
- Ein Installer mit Updates (Velopack wie FerretSharp ADR 0024) kam als eigenes Paket (WP-10, ADR 0007): seitdem baut ein Tag über `release.yml` das Release.

# Arbeitspakete – Index

Was in den abgeschlossenen Paketen gebaut und entschieden wurde, mit Nachträgen, Fallen und E2E-Erkenntnissen: je Paket eine Datei. Vor Änderungen an einem Bereich das Paket lesen, das ihn gebaut hat – die Spalte „Bereiche“ führt vom Code zum Paket. Geplante Pakete: `docs/roadmap.md`; übergreifende Fallen: `.claude/rules/`, `docs/e2e-testing.md`. Begründungen: `docs/decisions/`.

## Versionen

| Version | Inhalt | Pakete |
|---|---|---|
| **0.x – Fundament** | Übersicht aller Repos, Git-Grundaktionen, Auto-Fetch/Auto-Pull je Repo | WP-01 … |

## Übersicht

Reihenfolge wie die Releases.

| Paket | Datei | Inhalt | Bereiche | Release | ADR |
|---|---|---|---|---|---|
| WP-01 | [wp-01-foundation.md](wp-01-foundation.md) | Projekt nach dem Muster von FerretSharp, Spike übernommen: Übersicht, Fetch/Pull/Push, Stage/Commit, Branches, Auto-Fetch/Auto-Pull, Aktivität | alles (`Core/Git`, `Core/Board`, `Core/Sync`, `Core/Repos`, `UI`, `App`) | – | 0001–0005 |
